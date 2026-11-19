// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.FhirPackages
{
    /// <summary>
    /// Acquires and verifies an exact FHIR package from a deployment-configured source.
    /// </summary>
    public interface IFhirPackageAcquirer
    {
        /// <summary>
        /// Acquires the requested package without installing it.
        /// </summary>
        /// <param name="packageId">The exact npm package name.</param>
        /// <param name="version">The exact package version.</param>
        /// <param name="source">The administrator-configured source identifier.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The verified package.</returns>
        Task<FhirPackage> AcquireAsync(string packageId, string version, string source, CancellationToken cancellationToken);
    }
}
