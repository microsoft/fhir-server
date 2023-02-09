// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Specification.Source;
using Hl7.Fhir.Validation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Models;
using Polly;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    public class ProfileValidator : IProfileValidator, IHostedService
    {
        private readonly ILogger<ProfileValidator> _logger;
        private readonly IProvideProfilesForValidation _profilesResolver;
        private readonly Lazy<IResourceResolver> _resolver;
        private readonly CoreFeatureConfiguration _coreConfig;
        private readonly ValidateOperationConfiguration _options;

        public ProfileValidator(
            IProvideProfilesForValidation profilesResolver,
            IOptions<CoreFeatureConfiguration> coreFeatureConfiguration,
            IOptions<ValidateOperationConfiguration> options,
            ILogger<ProfileValidator> logger)
        {
            _profilesResolver = EnsureArg.IsNotNull(profilesResolver, nameof(profilesResolver));
            _options = EnsureArg.IsNotNull(options?.Value, nameof(options));
            _coreConfig = EnsureArg.IsNotNull(coreFeatureConfiguration?.Value, nameof(options));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));

            _resolver = new Lazy<IResourceResolver>(
                () => Policy.Handle<Exception>()
                    .WaitAndRetry(5, i => TimeSpan.FromSeconds(i * 2))
                    .Execute(Initialize),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        private IResourceResolver Initialize()
        {
            try
            {
                (string Server, string CorePackageName, string ExpansionsPackageName) packageVariables = GetFhirPackageVariables();

                var packagesToInclude = new HashSet<string>();

                if (_coreConfig.PackageConfiguration.IncludeDefaultPackages)
                {
                    packagesToInclude.Add(packageVariables.CorePackageName);
                    packagesToInclude.Add(packageVariables.ExpansionsPackageName);
                }

                foreach (var package in _coreConfig.PackageConfiguration.PackageNames)
                {
                    packagesToInclude.Add(package);
                }

                FhirPackageSource validationSource;

                if (!string.IsNullOrEmpty(_coreConfig.PackageConfiguration.PackageSource) &&
                    !_coreConfig.PackageConfiguration.PackageSource.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    // Allow local packages
                    var resolvedPath = Path.GetFullPath(_coreConfig.PackageConfiguration.PackageSource, Environment.CurrentDirectory);
                    validationSource = new FhirPackageSource(_coreConfig.PackageConfiguration.PackageNames.Select(x => Path.GetFullPath(x, resolvedPath)).ToArray());
                }
                else
                {
                    // Look for packages on an npm server
                    var server = string.IsNullOrEmpty(_coreConfig.PackageConfiguration.PackageSource) ? packageVariables.Server : _coreConfig.PackageConfiguration.PackageSource;
                    validationSource = new FhirPackageSource(server, packagesToInclude.ToArray());
                }

                // Ensure packages are downloaded to temp folder
                var patient = validationSource.ListResourceUris(ResourceType.Patient).ToArray();

                return new MultiResolver(_profilesResolver, new CachedResolver(validationSource, _options.CacheDurationInSeconds));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing profile validator");
                throw;
            }
        }

        internal static (string Server, string CorePackageName, string ExpansionsPackageName) GetFhirPackageVariables()
        {
            Type type = typeof(FhirPackageSource);
            FieldInfo serverField = type.GetField("FHIR_PACKAGE_SERVER", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo coreField = type.GetField("FHIR_CORE_PACKAGE_NAME", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo expansionsField = type.GetField("FHIR_CORE_EXPANSIONS_PACKAGE_NAME", BindingFlags.Static | BindingFlags.NonPublic);

            return (serverField?.GetValue(null) as string, coreField?.GetValue(null) as string, expansionsField?.GetValue(null) as string);
        }

        private Validator GetValidator()
        {
            var ctx = new ValidationSettings()
            {
                ResourceResolver = _resolver.Value,
                GenerateSnapshot = true,
                Trace = false,
                ResolveExternalReferences = false,
            };

            var validator = new Validator(ctx);

            return validator;
        }

        public OperationOutcomeIssue[] TryValidate(ITypedElement resource, string profile = null)
        {
            var validator = GetValidator();
            OperationOutcome result;
            if (!string.IsNullOrWhiteSpace(profile))
            {
                result = validator.Validate(resource, profile);
            }
            else
            {
                result = validator.Validate(resource);
            }

            var outcomeIssues = result.Issue.OrderBy(x => x.Severity)
                .Select(issue =>
                    new OperationOutcomeIssue(
                        issue.Severity?.ToString(),
                        issue.Code.ToString(),
                        diagnostics: issue.Diagnostics,
                        detailsText: issue.Details?.Text,
                        detailsCodes: issue.Details?.Coding != null ? new CodableConceptInfo(issue.Details.Coding.Select(x => new Coding(x.System, x.Code, x.Display))) : null,
                        expression: issue.Expression.ToArray()))
                .ToArray();

            return outcomeIssues;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Task.Run(
                () =>
                {
                    IResourceResolver resolver = _resolver.Value;
                    _logger.LogInformation($"Profile validator {resolver.GetType()} initialized");
                },
                cancellationToken).ConfigureAwait(false);

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
