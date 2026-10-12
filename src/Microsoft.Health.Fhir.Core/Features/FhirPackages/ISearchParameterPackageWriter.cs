// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.FhirPackages
{
    /// <summary>
    /// Persists a validated SearchParameter set from a package.
    /// </summary>
    /// <remarks>
    /// The installer calls this only after the complete set has been validated. The implementation owns
    /// atomic persistence, status, ownership and the applied-package record.
    /// </remarks>
    public interface ISearchParameterPackageWriter
    {
        /// <summary>
        /// Writes the package's SearchParameters as one change.
        /// </summary>
        /// <param name="package">The package being installed.</param>
        /// <param name="searchParameters">The validated SearchParameter resources.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the SearchParameters are written.</returns>
        Task WriteAsync(FhirPackage package, IReadOnlyList<ResourceElement> searchParameters, CancellationToken cancellationToken);
    }
}
