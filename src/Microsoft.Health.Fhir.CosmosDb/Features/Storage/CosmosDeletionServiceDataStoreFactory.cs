// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using EnsureThat;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public sealed class CosmosDeletionServiceDataStoreFactory : IDeletionServiceDataStoreFactory
    {
        // Cosmos DB requires a new fresh instance of the data store for each operation to ensure that long running operations will
        // not have their tokens expire. Therefore, we use a scope provider to create a new instance on each call.
        private readonly IScopeProvider<IFhirDataStore> _dataStore;
        private readonly ConcurrentDictionary<Guid, IScoped<IFhirDataStore>> _dataStoresByScope;

        public CosmosDeletionServiceDataStoreFactory(IScopeProvider<IFhirDataStore> dataStore)
        {
            _dataStore = EnsureArg.IsNotNull(dataStore, nameof(dataStore));
            _dataStoresByScope = new ConcurrentDictionary<Guid, IScoped<IFhirDataStore>>();
        }

        public IFhirDataStore GetDataStore(Guid scopeId)
        {
            IScoped<IFhirDataStore> scopedDataStore = _dataStore.Invoke();

            if (!_dataStoresByScope.TryAdd(scopeId, scopedDataStore))
            {
                _dataStoresByScope[scopeId].Dispose();

                if (!_dataStoresByScope.TryAdd(scopeId, scopedDataStore))
                {
                    throw new InvalidOperationException($"Failed to create new Data Store for scope '{scopeId}'.");
                }
            }

            return scopedDataStore.Value;
        }

        public void ReleaseDataStore(Guid scopeId)
        {
            if (_dataStoresByScope.TryGetValue(scopeId, out IScoped<IFhirDataStore> scopedDataStore))
            {
                scopedDataStore.Dispose();
                _dataStoresByScope.TryRemove(scopeId, out _);
            }
        }
    }
}
