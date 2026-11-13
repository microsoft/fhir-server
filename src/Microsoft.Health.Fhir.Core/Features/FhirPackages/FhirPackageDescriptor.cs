// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.FhirPackages
{
    /// <summary>
    /// Identifies the exact package a caller expects to read, and the integrity the source reported for it.
    /// </summary>
    public sealed class FhirPackageDescriptor
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FhirPackageDescriptor"/> class.
        /// </summary>
        /// <param name="packageId">The expected package name.</param>
        /// <param name="version">The expected exact package version.</param>
        /// <param name="integrity">The expected SHA-512 Subresource Integrity value, for example from npm <c>dist.integrity</c>.</param>
        public FhirPackageDescriptor(string packageId, string version, string integrity)
        {
            EnsureArg.IsNotNullOrWhiteSpace(packageId, nameof(packageId));
            EnsureArg.IsNotNullOrWhiteSpace(version, nameof(version));
            EnsureArg.IsNotNullOrWhiteSpace(integrity, nameof(integrity));

            PackageId = packageId;
            Version = version;
            Integrity = integrity;
        }

        /// <summary>
        /// Gets the expected package name.
        /// </summary>
        public string PackageId { get; }

        /// <summary>
        /// Gets the expected exact package version.
        /// </summary>
        public string Version { get; }

        /// <summary>
        /// Gets the expected SHA-512 Subresource Integrity value.
        /// </summary>
        public string Integrity { get; }
    }
}
