// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.FhirPackages
{
    /// <summary>
    /// Dispatches package resources to resource-type handlers, validates every handler's set, and writes only
    /// when the whole package is valid.
    /// </summary>
    public sealed class FhirPackageInstaller : IFhirPackageInstaller
    {
        private readonly IReadOnlyDictionary<string, IFhirPackageResourceHandler> _handlers;
        private readonly ILogger<FhirPackageInstaller> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="FhirPackageInstaller"/> class.
        /// </summary>
        /// <param name="handlers">The registered resource-type handlers; at most one per resource type.</param>
        /// <param name="logger">The logger.</param>
        public FhirPackageInstaller(IEnumerable<IFhirPackageResourceHandler> handlers, ILogger<FhirPackageInstaller> logger)
        {
            EnsureArg.IsNotNull(handlers, nameof(handlers));
            EnsureArg.IsNotNull(logger, nameof(logger));

            var byType = new Dictionary<string, IFhirPackageResourceHandler>(StringComparer.Ordinal);
            foreach (IFhirPackageResourceHandler handler in handlers)
            {
                if (!byType.TryAdd(handler.ResourceType, handler))
                {
                    throw new InvalidOperationException($"More than one package handler is registered for resource type '{handler.ResourceType}'.");
                }
            }

            _handlers = byType;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task<FhirPackageInstallResult> InstallAsync(FhirPackage package, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(package, nameof(package));

            var handled = new SortedDictionary<string, List<FhirPackageEntry>>(StringComparer.Ordinal);
            var ignored = new SortedDictionary<string, int>(StringComparer.Ordinal);

            foreach (FhirPackageEntry entry in package.ResourceEntries)
            {
                if (entry.ResourceType != null && _handlers.ContainsKey(entry.ResourceType))
                {
                    if (!handled.TryGetValue(entry.ResourceType, out List<FhirPackageEntry> entries))
                    {
                        entries = new List<FhirPackageEntry>();
                        handled.Add(entry.ResourceType, entries);
                    }

                    entries.Add(entry);
                }
                else
                {
                    CountIgnored(entry);
                }
            }

            foreach (FhirPackageEntry entry in package.IgnoredEntries)
            {
                CountIgnored(entry);
            }

            if (handled.Count == 0)
            {
                throw new FhirPackageException(
                    OperationOutcomeConstants.IssueType.Invalid,
                    string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageNoInstallableResources, package.PackageId, package.Version, InstalledTypes()));
            }

            // Phase 1: validate everything before anything is written.
            var plans = new List<IFhirPackageInstallPlan>();
            var issues = new List<OperationOutcomeIssue>();
            foreach (KeyValuePair<string, List<FhirPackageEntry>> group in handled)
            {
                try
                {
                    plans.Add(await _handlers[group.Key].ValidateAsync(group.Value, package, cancellationToken));
                }
                catch (FhirPackageException ex)
                {
                    issues.AddRange(ex.Issues);
                }
            }

            if (issues.Count > 0)
            {
                _logger.LogWarning(
                    "FHIR package {PackageId} version {Version} was rejected with {IssueCount} issue(s).",
                    package.PackageId,
                    package.Version,
                    issues.Count);

                throw new FhirPackageException(issues);
            }

            // Phase 2: write. Nothing has been written yet, so cancellation is still clean here.
            cancellationToken.ThrowIfCancellationRequested();

            var installed = new SortedDictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (IFhirPackageInstallPlan plan in plans)
            {
                await plan.ApplyAsync(cancellationToken);
                installed[plan.ResourceType] = plan.CanonicalUrls;
            }

            _logger.LogInformation(
                "Installed FHIR package {PackageId} version {Version}: {InstalledCount} resource(s) installed, {IgnoredCount} ignored.",
                package.PackageId,
                package.Version,
                installed.Values.Sum(x => x.Count),
                ignored.Values.Sum());

            return new FhirPackageInstallResult(package, installed, ignored, _handlers.Keys.ToArray());

            void CountIgnored(FhirPackageEntry entry)
            {
                // Non-resource JSON such as .index.json is package metadata, not ignored content.
                if (entry.ResourceType != null)
                {
                    ignored[entry.ResourceType] = ignored.GetValueOrDefault(entry.ResourceType) + 1;
                }
            }

            string InstalledTypes() => string.Join(", ", _handlers.Keys.OrderBy(x => x, StringComparer.Ordinal));
        }
    }
}
