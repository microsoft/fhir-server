// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.FhirPackages
{
    /// <summary>
    /// The outcome of a successful package installation.
    /// </summary>
    public sealed class FhirPackageInstallResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FhirPackageInstallResult"/> class.
        /// </summary>
        /// <param name="package">The installed package.</param>
        /// <param name="installedResources">Canonical URLs installed, keyed by resource type.</param>
        /// <param name="ignoredResourceTypes">Counts of resources that were not installed, keyed by resource type.</param>
        /// <param name="installedResourceTypes">The resource types the server installs from packages.</param>
        public FhirPackageInstallResult(
            FhirPackage package,
            IReadOnlyDictionary<string, IReadOnlyList<string>> installedResources,
            IReadOnlyDictionary<string, int> ignoredResourceTypes,
            IReadOnlyCollection<string> installedResourceTypes)
        {
            EnsureArg.IsNotNull(package, nameof(package));
            EnsureArg.IsNotNull(installedResources, nameof(installedResources));
            EnsureArg.IsNotNull(ignoredResourceTypes, nameof(ignoredResourceTypes));
            EnsureArg.IsNotNull(installedResourceTypes, nameof(installedResourceTypes));

            Descriptor = package.Descriptor;
            InstalledResources = installedResources;
            IgnoredResourceTypes = ignoredResourceTypes;
            IgnoredDependencies = package.IgnoredDependencies;
            Issues = BuildIssues(installedResourceTypes);
        }

        /// <summary>
        /// Gets the verified identity and integrity of the installed package.
        /// </summary>
        public FhirPackageDescriptor Descriptor { get; }

        /// <summary>
        /// Gets the canonical URLs installed, keyed by resource type.
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlyList<string>> InstalledResources { get; }

        /// <summary>
        /// Gets the counts of resources that were not installed, keyed by resource type.
        /// </summary>
        public IReadOnlyDictionary<string, int> IgnoredResourceTypes { get; }

        /// <summary>
        /// Gets the declared non-core dependencies, which were not resolved.
        /// </summary>
        public IReadOnlyList<string> IgnoredDependencies { get; }

        /// <summary>
        /// Gets information issues describing the content that was not installed.
        /// </summary>
        public IReadOnlyList<OperationOutcomeIssue> Issues { get; }

        private List<OperationOutcomeIssue> BuildIssues(IReadOnlyCollection<string> installedResourceTypes)
        {
            string installedTypes = string.Join(", ", installedResourceTypes.OrderBy(x => x, StringComparer.Ordinal));
            var issues = new List<OperationOutcomeIssue>();

            foreach (KeyValuePair<string, int> ignored in IgnoredResourceTypes.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                issues.Add(new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Information,
                    OperationOutcomeConstants.IssueType.Informational,
                    string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageIgnoredResourceType, ignored.Value, ignored.Key, installedTypes)));
            }

            foreach (string dependency in IgnoredDependencies)
            {
                issues.Add(new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Information,
                    OperationOutcomeConstants.IssueType.Informational,
                    string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageIgnoredDependency, dependency)));
            }

            return issues;
        }
    }
}
