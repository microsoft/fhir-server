// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.FhirPackages
{
    /// <summary>
    /// Installs the supported resources of a verified FHIR package.
    /// </summary>
    public interface IFhirPackageInstaller
    {
        /// <summary>
        /// Validates every installable resource in the package and, only if all are valid, writes them.
        /// </summary>
        /// <param name="package">A package whose archive and manifest have already been verified.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>What was installed and what was ignored.</returns>
        /// <exception cref="FhirPackageException">The package was rejected and nothing was written.</exception>
        Task<FhirPackageInstallResult> InstallAsync(FhirPackage package, CancellationToken cancellationToken);
    }
}
