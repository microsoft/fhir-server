// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.FhirPackages
{
    /// <summary>
    /// Reads a FHIR NPM package tarball safely, without writing anything to disk.
    /// </summary>
    public interface IFhirPackageReader
    {
        /// <summary>
        /// Verifies the tarball's integrity, extracts it in memory within the configured limits, checks the manifest
        /// against the descriptor and the server's FHIR version, and classifies its JSON entries.
        /// </summary>
        /// <param name="tarball">The gzip-compressed package tarball, read from its current position to the end.</param>
        /// <param name="descriptor">The package the caller expects, including its expected integrity.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The verified package.</returns>
        /// <exception cref="FhirPackageException">The package was rejected.</exception>
        Task<FhirPackage> ReadAsync(Stream tarball, FhirPackageDescriptor descriptor, CancellationToken cancellationToken);
    }
}
