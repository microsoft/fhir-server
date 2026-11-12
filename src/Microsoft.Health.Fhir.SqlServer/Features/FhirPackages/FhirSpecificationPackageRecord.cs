// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;

namespace Microsoft.Health.Fhir.SqlServer.Features.FhirPackages
{
    /// <summary>
    /// The applied-package record: which package version is installed and which canonical URLs it owns.
    /// </summary>
    public sealed class FhirSpecificationPackageRecord
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FhirSpecificationPackageRecord"/> class.
        /// </summary>
        /// <param name="packageId">The package id.</param>
        /// <param name="version">The exact installed version.</param>
        /// <param name="integrity">The integrity hash of the installed package.</param>
        /// <param name="canonicalUrls">The canonical URLs the package owns.</param>
        public FhirSpecificationPackageRecord(string packageId, string version, string integrity, IReadOnlyList<string> canonicalUrls)
        {
            PackageId = EnsureArg.IsNotNullOrWhiteSpace(packageId, nameof(packageId));
            Version = EnsureArg.IsNotNullOrWhiteSpace(version, nameof(version));
            Integrity = integrity;
            CanonicalUrls = EnsureArg.IsNotNull(canonicalUrls, nameof(canonicalUrls));
        }

        /// <summary>
        /// Gets the package id.
        /// </summary>
        public string PackageId { get; }

        /// <summary>
        /// Gets the exact installed version.
        /// </summary>
        public string Version { get; }

        /// <summary>
        /// Gets the integrity hash of the installed package, or <c>null</c> if the source didn't provide one.
        /// </summary>
        public string Integrity { get; }

        /// <summary>
        /// Gets the canonical URLs the package owns.
        /// </summary>
        public IReadOnlyList<string> CanonicalUrls { get; }

        /// <summary>
        /// Determines whether <paramref name="other"/> records the same package content: same id, version and integrity.
        /// </summary>
        /// <param name="other">The record to compare with.</param>
        /// <returns><c>true</c> if installing <paramref name="other"/> over this record is a no-op.</returns>
        public bool IsSameContentAs(FhirSpecificationPackageRecord other)
        {
            return other != null
                && string.Equals(PackageId, other.PackageId, StringComparison.Ordinal)
                && string.Equals(Version, other.Version, StringComparison.Ordinal)
                && string.Equals(Integrity, other.Integrity, StringComparison.Ordinal);
        }
    }
}
