// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.FhirPackages
{
    /// <summary>
    /// Verifies persisted package SearchParameters before a new write or a recovered completion.
    /// </summary>
    public interface IFhirPackageInstallVerifier
    {
        /// <summary>
        /// Identifies missing entries and rejects conflicting or non-searchable existing entries.
        /// </summary>
        /// <param name="package">The verified package.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The SearchParameter installation state.</returns>
        Task<FhirPackageInstallInspection> InspectAsync(FhirPackage package, CancellationToken cancellationToken);
    }

    /// <summary>
    /// The persisted SearchParameter state for a package.
    /// </summary>
    public sealed class FhirPackageInstallInspection
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FhirPackageInstallInspection"/> class.
        /// </summary>
        /// <param name="canonicalUrls">All SearchParameter URLs in the package.</param>
        /// <param name="missingEntries">Entries not yet present in the store.</param>
        /// <param name="lastUpdated">The latest status update among existing package SearchParameters.</param>
        public FhirPackageInstallInspection(IReadOnlyList<string> canonicalUrls, IReadOnlyList<FhirPackageEntry> missingEntries, DateTimeOffset? lastUpdated)
        {
            CanonicalUrls = EnsureArg.IsNotNull(canonicalUrls, nameof(canonicalUrls));
            MissingEntries = EnsureArg.IsNotNull(missingEntries, nameof(missingEntries));
            LastUpdated = lastUpdated;
        }

        /// <summary>
        /// Gets the canonical URLs requested by the package.
        /// </summary>
        public IReadOnlyList<string> CanonicalUrls { get; }

        /// <summary>
        /// Gets the entries that still need to be installed.
        /// </summary>
        public IReadOnlyList<FhirPackageEntry> MissingEntries { get; }

        /// <summary>
        /// Gets the latest persisted status update, or <c>null</c> if none are installed.
        /// </summary>
        public DateTimeOffset? LastUpdated { get; }
    }
}
