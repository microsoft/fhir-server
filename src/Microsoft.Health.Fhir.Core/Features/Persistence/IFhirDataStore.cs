// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public interface IFhirDataStore
    {
        Task<IDictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>> MergeAsync(IReadOnlyList<ResourceWrapperOperation> resources, CancellationToken cancellationToken);

        Task<IDictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>> MergeAsync(IReadOnlyList<ResourceWrapperOperation> resources, MergeOptions mergeOptions, CancellationToken cancellationToken);

        Task<IReadOnlyList<ResourceWrapper>> GetAsync(IReadOnlyList<ResourceKey> keys, CancellationToken cancellationToken);

        Task<UpsertOutcome> UpsertAsync(ResourceWrapperOperation resource, CancellationToken cancellationToken);

        Task<ResourceWrapper> GetAsync(ResourceKey key, CancellationToken cancellationToken);

        /// <summary>
        /// Hard deletes a resource.
        /// </summary>
        /// <param name="key">Identifier of the resource</param>
        /// <param name="keepCurrentVersion">Keeps the current version of the resource, only deleting history</param>
        /// <param name="allowPartialSuccess">Only for Cosmos. Allows for a delete to partially succeed if it fails to delete all versions of a resource in one try.</param>
        /// <param name="cancellationToken">Cancellation Token</param>
        /// <returns>Async Task</returns>
        Task HardDeleteAsync(ResourceKey key, bool keepCurrentVersion, bool allowPartialSuccess, CancellationToken cancellationToken);

        Task BulkUpdateSearchParameterIndicesAsync(IReadOnlyCollection<ResourceWrapper> resources, CancellationToken cancellationToken);

        Task<ResourceWrapper> UpdateSearchParameterIndicesAsync(ResourceWrapper resourceWrapper, CancellationToken cancellationToken);

        Task<int?> GetProvisionedDataStoreCapacityAsync(CancellationToken cancellationToken);
    }
}
