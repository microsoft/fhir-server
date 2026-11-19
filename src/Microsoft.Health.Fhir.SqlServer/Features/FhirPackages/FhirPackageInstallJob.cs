// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.FhirPackages;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.JobManagement;
using Microsoft.Health.SqlServer.Features.Client;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.SqlServer.Features.FhirPackages
{
    /// <summary>
    /// Installs or recovers an exact FHIR package and waits for active SQL hosts to refresh their caches.
    /// </summary>
    [JobTypeId((int)JobType.FhirPackageInstall)]
    public sealed class FhirPackageInstallJob : IJob
    {
        private const int MaxConcurrentInstallAttempts = 3;
        private readonly IServiceProvider _services;
        private readonly ISearchParameterStatusManager _statusManager;
        private readonly FhirPackageInstallJobConfiguration _configuration;
        private readonly int _refreshIntervalSeconds;
        private readonly ILogger<FhirPackageInstallJob> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="FhirPackageInstallJob"/> class.
        /// </summary>
        /// <param name="services">The job scope, used to resolve the optional package feature at execution time.</param>
        /// <param name="statusManager">Checks cache convergence across active hosts.</param>
        /// <param name="operationsConfiguration">The package queue and convergence settings.</param>
        /// <param name="coreFeatures">The SearchParameter cache refresh interval.</param>
        /// <param name="logger">The job logger.</param>
        public FhirPackageInstallJob(
            IServiceProvider services,
            ISearchParameterStatusManager statusManager,
            IOptions<OperationsConfiguration> operationsConfiguration,
            IOptions<CoreFeatureConfiguration> coreFeatures,
            ILogger<FhirPackageInstallJob> logger)
        {
            _services = EnsureArg.IsNotNull(services, nameof(services));
            _statusManager = EnsureArg.IsNotNull(statusManager, nameof(statusManager));
            _configuration = EnsureArg.IsNotNull(operationsConfiguration?.Value?.FhirPackageInstall, nameof(operationsConfiguration));
            _refreshIntervalSeconds = EnsureArg.IsNotNull(coreFeatures?.Value, nameof(coreFeatures)).SearchParameterCacheRefreshIntervalSeconds;
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        /// <inheritdoc />
        public async Task<string> ExecuteAsync(JobInfo jobInfo, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));
            FhirPackageInstallJobDefinition definition = jobInfo.DeserializeDefinition<FhirPackageInstallJobDefinition>();
            var result = new FhirPackageInstallJobResult
            {
                PackageId = definition?.PackageId,
                Version = definition?.Version,
                State = FhirPackageInstallJobState.Verifying,
            };

            if (definition == null || string.IsNullOrWhiteSpace(definition.PackageId) ||
                string.IsNullOrWhiteSpace(definition.Version) || string.IsNullOrWhiteSpace(definition.Source) ||
                definition.RetryOfJobId <= 0)
            {
                result.Message = "The package install job definition is invalid.";
                throw new JobExecutionException(result.Message, result, isCustomerCaused: true);
            }

            if (_refreshIntervalSeconds <= 0 || _configuration.CacheUpdateMaxWaitMultiplier <= 0 || _configuration.ActiveHostsEventsMultiplier <= 0)
            {
                result.Message = "The package install cache convergence configuration must use positive intervals.";
                throw new JobExecutionException(result.Message, result, isCustomerCaused: false);
            }

            try
            {
                // JobFactory instantiates every discovered job even when this feature is disabled.
                IFhirPackageAcquirer acquirer = _services.GetRequiredService<IFhirPackageAcquirer>();
                IFhirPackageInstaller installer = _services.GetRequiredService<IFhirPackageInstaller>();
                IFhirPackageInstallVerifier verifier = _services.GetRequiredService<IFhirPackageInstallVerifier>();

                FhirPackage package = await acquirer.AcquireAsync(definition.PackageId, definition.Version, definition.Source, cancellationToken);
                result.Integrity = package.Descriptor.Integrity;

                FhirPackageInstallInspection inspection = await InstallOrVerifyAsync(package, installer, verifier, cancellationToken);
                result.SearchParameterUrls = inspection.CanonicalUrls;
                result.Issues = CreateInformationalIssues(package, inspection.CanonicalUrls);
                result.State = FhirPackageInstallJobState.Committed;

                if (!await WaitForCacheConvergenceAsync(package, inspection.LastUpdated.Value, verifier, cancellationToken))
                {
                    result.IsRetryable = true;
                    result.Message = "SearchParameter cache convergence timed out after the package was committed. Retry the install job.";
                    throw new JobExecutionException(result.Message, result, isCustomerCaused: false);
                }

                result.State = FhirPackageInstallJobState.Converged;
                _logger.LogInformation(
                    "FHIR package install job {JobId} converged for {PackageId} version {Version}.",
                    jobInfo.Id,
                    definition.PackageId,
                    definition.Version);
                return JsonConvert.SerializeObject(result);
            }
            catch (FhirPackageAcquisitionException ex)
            {
                throw Failure(result, ex, ex.IsRetryable, !ex.IsRetryable);
            }
            catch (FhirPackageWriteConflictException ex)
            {
                result.Issues = ex.Issues.ToArray();
                throw Failure(result, ex, ex.IsRetryable, !ex.IsRetryable);
            }
            catch (FhirPackageException ex)
            {
                result.Issues = ex.Issues.ToArray();
                throw Failure(result, ex, isRetryable: false, isCustomerCaused: true);
            }
            catch (FhirPackageStoreNotEmptyException ex)
            {
                result.Issues = ex.Issues.ToArray();
                throw Failure(result, ex, isRetryable: false, isCustomerCaused: true);
            }
            catch (SqlException ex)
            {
                bool isRetryable = ex.IsSqlTransientException() || ex.IsRetriable() || ex.IsExecutionTimeout();
                throw Failure(result, ex, isRetryable, isCustomerCaused: false);
            }
        }

        private static async Task<FhirPackageInstallInspection> InstallOrVerifyAsync(
            FhirPackage package,
            IFhirPackageInstaller installer,
            IFhirPackageInstallVerifier verifier,
            CancellationToken cancellationToken)
        {
            FhirPackageInstallInspection inspection = await verifier.InspectAsync(package, cancellationToken);
            if (inspection.CanonicalUrls.Count == 0)
            {
                // Preserve the installer's normal diagnostic for packages with no installable resources.
                await installer.InstallAsync(package, cancellationToken);
                throw new InvalidOperationException("The package installer returned without installing a SearchParameter.");
            }

            for (int attempt = 0; inspection.MissingEntries.Count > 0 && attempt < MaxConcurrentInstallAttempts; attempt++)
            {
                int missingBefore = inspection.MissingEntries.Count;
                var missingEntries = inspection.MissingEntries.ToHashSet();
                var pendingPackage = new FhirPackage(
                    package.Descriptor,
                    package.ResourceEntries.Where(entry => entry.ResourceType != KnownResourceTypes.SearchParameter || missingEntries.Contains(entry)).ToList(),
                    package.IgnoredEntries,
                    package.IgnoredDependencies);

                try
                {
                    await installer.InstallAsync(pendingPackage, cancellationToken);
                }
                catch (FhirPackageWriteConflictException)
                {
                    FhirPackageInstallInspection afterConflict = await verifier.InspectAsync(package, cancellationToken);
                    if (afterConflict.MissingEntries.Count >= missingBefore)
                    {
                        throw;
                    }

                    inspection = afterConflict;
                    continue;
                }
                catch (FhirPackageStoreNotEmptyException)
                {
                    FhirPackageInstallInspection afterConflict = await verifier.InspectAsync(package, cancellationToken);
                    if (afterConflict.MissingEntries.Count != 0)
                    {
                        throw;
                    }

                    inspection = afterConflict;
                    break;
                }

                inspection = await verifier.InspectAsync(package, cancellationToken);
                if (inspection.MissingEntries.Count >= missingBefore)
                {
                    throw new FhirPackageWriteConflictException("The package write completed but its SearchParameters could not be verified. Retry the install job.", isRetryable: true);
                }
            }

            if (inspection.MissingEntries.Count != 0 || !inspection.LastUpdated.HasValue)
            {
                throw new FhirPackageWriteConflictException("Concurrent SearchParameter changes prevented package installation. Retry the install job.", isRetryable: true);
            }

            return inspection;
        }

        private async Task<bool> WaitForCacheConvergenceAsync(
            FhirPackage package,
            DateTimeOffset targetVersion,
            IFhirPackageInstallVerifier verifier,
            CancellationToken cancellationToken)
        {
            DateTime updateEventsSince = DateTime.UtcNow;
            string target = targetVersion.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
            var interval = TimeSpan.FromSeconds(_refreshIntervalSeconds);
            var timeout = TimeSpan.FromSeconds((double)_refreshIntervalSeconds * _configuration.CacheUpdateMaxWaitMultiplier);
            var activeHostWindow = TimeSpan.FromSeconds((double)_refreshIntervalSeconds * _configuration.ActiveHostsEventsMultiplier);
            var stopwatch = Stopwatch.StartNew();

            while (stopwatch.Elapsed < timeout)
            {
                CacheConsistencyResult consistency = await _statusManager.CheckCacheConsistencyAsync(
                    updateEventsSince,
                    DateTime.UtcNow.Subtract(activeHostWindow),
                    cancellationToken,
                    SearchParameterCacheFingerprint.UpdateProcessName);

                string proof = consistency.SearchParameterVersion;
                int separator = proof?.IndexOf('|', StringComparison.Ordinal) ?? -1;
                if (consistency.IsConsistent && separator > 0 &&
                    string.CompareOrdinal(proof[..separator], target) >= 0)
                {
                    FhirPackageInstallInspection current = await verifier.InspectAsync(package, cancellationToken);
                    if (current.MissingEntries.Count > 0 || !current.LastUpdated.HasValue)
                    {
                        throw new FhirPackageWriteConflictException("The committed package SearchParameters changed during cache convergence. Retry the install job.", isRetryable: true);
                    }

                    string currentVersion = current.LastUpdated.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
                    var definitions = _services.GetRequiredService<ISearchParameterDefinitionManager>();
                    string fingerprint = SearchParameterCacheFingerprint.Calculate(definitions);
                    if (string.CompareOrdinal(proof[..separator], currentVersion) >= 0 &&
                        string.Equals(proof[(separator + 1)..], fingerprint, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                _logger.LogInformation(
                    "FHIR package cache convergence: {ConvergedHosts}/{ActiveHosts} active hosts have matching updates.",
                    consistency.ConvergedHosts,
                    consistency.ActiveHosts);
                TimeSpan remaining = timeout - stopwatch.Elapsed;
                if (remaining > TimeSpan.Zero)
                {
                    await Task.Delay(remaining < interval ? remaining : interval, cancellationToken);
                }
            }

            return false;
        }

        private static IReadOnlyList<OperationOutcomeIssue> CreateInformationalIssues(FhirPackage package, IReadOnlyList<string> canonicalUrls)
        {
            var ignored = package.ResourceEntries
                .Where(entry => entry.ResourceType != KnownResourceTypes.SearchParameter)
                .Concat(package.IgnoredEntries)
                .Where(entry => entry.ResourceType != null)
                .GroupBy(entry => entry.ResourceType, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

            var installed = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                [KnownResourceTypes.SearchParameter] = canonicalUrls,
            };

            return new FhirPackageInstallResult(package, installed, ignored, new[] { KnownResourceTypes.SearchParameter }).Issues;
        }

        private static JobExecutionException Failure(FhirPackageInstallJobResult result, Exception exception, bool isRetryable, bool isCustomerCaused)
        {
            result.IsRetryable = isRetryable;
            result.Message = exception.Message;
            return new JobExecutionException(result.Message, result, exception, isCustomerCaused);
        }
    }
}
