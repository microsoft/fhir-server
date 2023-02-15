// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public interface IFhirDataStore
    {
        Task TryLogEvent(string process, string status, string text, DateTime startDate, CancellationToken cancellationToken);

        string GetRawResourceFromAdls(long transactionId, int offsetInFile);

        Task<IDictionary<ResourceKey, UpsertOutcome>> MergeAsync(IReadOnlyList<ResourceWrapperOperation> resources, CancellationToken cancellationToken);

        Task<IReadOnlyList<ResourceWrapper>> GetAsync(IReadOnlyList<ResourceKey> keys, CancellationToken cancellationToken);

        Task<UpsertOutcome> UpsertAsync(ResourceWrapperOperation resource, CancellationToken cancellationToken);

        Task<ResourceWrapper> GetAsync(ResourceKey key, CancellationToken cancellationToken);

        Task HardDeleteAsync(ResourceKey key, bool keepCurrentVersion, CancellationToken cancellationToken);

        Task BulkUpdateSearchParameterIndicesAsync(IReadOnlyCollection<ResourceWrapper> resources, CancellationToken cancellationToken);

        Task<ResourceWrapper> UpdateSearchParameterIndicesAsync(ResourceWrapper resourceWrapper, WeakETag weakETag, CancellationToken cancellationToken);

        Task<int?> GetProvisionedDataStoreCapacityAsync(CancellationToken cancellationToken);
    }
}
