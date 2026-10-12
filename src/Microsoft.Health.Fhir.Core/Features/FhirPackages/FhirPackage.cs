// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.FhirPackages
{
    /// <summary>
    /// A FHIR NPM package whose archive and manifest have already been verified by a package reader.
    /// </summary>
    public sealed class FhirPackage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FhirPackage"/> class.
        /// </summary>
        /// <param name="packageId">The package name from the verified manifest.</param>
        /// <param name="version">The exact package version from the verified manifest.</param>
        /// <param name="integrity">The verified SHA-512 SRI integrity of the package tarball.</param>
        /// <param name="resourceEntries">Resource entries at the package root, which are candidates for installation.</param>
        /// <param name="ignoredEntries">Entries that are never installed, such as examples and other sub-folder content.</param>
        /// <param name="ignoredDependencies">Declared package dependencies other than the FHIR core package; they are never resolved.</param>
        public FhirPackage(
            string packageId,
            string version,
            string integrity,
            IReadOnlyList<FhirPackageEntry> resourceEntries,
            IReadOnlyList<FhirPackageEntry> ignoredEntries = null,
            IReadOnlyList<string> ignoredDependencies = null)
        {
            EnsureArg.IsNotNullOrWhiteSpace(packageId, nameof(packageId));
            EnsureArg.IsNotNullOrWhiteSpace(version, nameof(version));
            EnsureArg.IsNotNull(resourceEntries, nameof(resourceEntries));

            PackageId = packageId;
            Version = version;
            Integrity = integrity;
            ResourceEntries = resourceEntries;
            IgnoredEntries = ignoredEntries ?? Array.Empty<FhirPackageEntry>();
            IgnoredDependencies = ignoredDependencies ?? Array.Empty<string>();
        }

        /// <summary>
        /// Gets the package name.
        /// </summary>
        public string PackageId { get; }

        /// <summary>
        /// Gets the exact package version.
        /// </summary>
        public string Version { get; }

        /// <summary>
        /// Gets the verified SHA-512 SRI integrity of the package tarball.
        /// </summary>
        public string Integrity { get; }

        /// <summary>
        /// Gets the resource entries at the package root.
        /// </summary>
        public IReadOnlyList<FhirPackageEntry> ResourceEntries { get; }

        /// <summary>
        /// Gets the entries that are never installed.
        /// </summary>
        public IReadOnlyList<FhirPackageEntry> IgnoredEntries { get; }

        /// <summary>
        /// Gets the declared non-core dependencies, which are never resolved.
        /// </summary>
        public IReadOnlyList<string> IgnoredDependencies { get; }
    }
}
