// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public sealed class CosmosDeletionServiceDataStoreFactory : IDeletionServiceDataStoreFactory
    {
        // Cosmos DB requires a new fresh instance of the data store for each operation to ensure that long running operations will
        // not have their tokens expire. Therefore, we use a scope provider to create a new instance on each call.
        private readonly IScopeProvider<IFhirDataStore> _dataStore;

        public CosmosDeletionServiceDataStoreFactory(IScopeProvider<IFhirDataStore> dataStore)
        {
            _dataStore = EnsureArg.IsNotNull(dataStore, nameof(dataStore));
        }

        public IFhirDataStore GetDataStore()
        {
            using (var scope = _dataStore.Invoke())
            {
                // The IScoped is disposed here to avoid leaking any other resources that might be held by the scope.
                // IFhirDataStore is expected to be a lightweight proxy that does not hold any resources itself.
                return scope.Value;
            }
        }
    }
}
