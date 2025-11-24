// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Core.Features.Search.Parameters
{
    public interface ISearchParameterOperations
    {
        Task AddSearchParameterAsync(ITypedElement searchParam, CancellationToken cancellationToken);

        /// <summary>
        /// Marks a SearchParameter for deletion. This can be either a soft delete (PendingDelete) or hard delete (PendingHardDelete).
        /// For hard deletes, a reindex operation is required to finalize the deletion by removing all index entries and the SearchParam registry entry.
        /// </summary>
        /// <param name="searchParamResource">The SearchParameter resource to delete</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <param name="ignoreSearchParameterNotSupportedException">Whether to ignore SearchParameterNotSupportedException</param>
        /// <param name="isHardDelete">If true, marks for hard deletion (PendingHardDelete); otherwise marks for soft deletion (PendingDelete)</param>
        /// <returns>Task representing the async operation</returns>
        Task DeleteSearchParameterAsync(RawResource searchParamResource, CancellationToken cancellationToken, bool ignoreSearchParameterNotSupportedException = false, bool isHardDelete = false);

        Task UpdateSearchParameterAsync(ITypedElement searchParam, RawResource previousSearchParam, CancellationToken cancellationToken);

        /// <summary>
        /// This method should be called periodically to get any updates to SearchParameters
        /// added to the DB by other service instances.
        /// It should also be called when a user starts a reindex job
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>A task.</returns>
        Task GetAndApplySearchParameterUpdates(CancellationToken cancellationToken);

        string GetResourceTypeSearchParameterHashMap(string resourceType);
    }
}
