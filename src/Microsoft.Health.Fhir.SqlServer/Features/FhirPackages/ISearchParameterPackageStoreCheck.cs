// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.SqlServer.Features.FhirPackages
{
    /// <summary>
    /// Checks the empty-store precondition for writing package SearchParameters as <c>Enabled</c> without a reindex.
    /// </summary>
    public interface ISearchParameterPackageStoreCheck
    {
        /// <summary>
        /// Determines whether the store contains any resource other than a SearchParameter.
        /// </summary>
        /// <remarks>
        /// SearchParameter resources don't count, because earlier package installs write them. Must run inside the
        /// ambient SQL transaction that writes the package, and must keep the answer true until that transaction ends.
        /// </remarks>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns><c>true</c> if the store contains data that a reindex would need to cover.</returns>
        Task<bool> ContainsDataAsync(CancellationToken cancellationToken);
    }
}
