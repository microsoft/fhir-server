// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Scoping;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public sealed class CosmosDeletionServiceDataStoreFactory : IDeletionServiceDataStoreFactory
    {
        // Cosmos DB requires a new fresh instance of the data store for each operation to ensure that long running operations will
        // not have their tokens expire. Therefore, we use a scope provider to create a new instance on each call.
        private readonly IOwnedScopeFactory<IFhirDataStore> _dataStore;

        public CosmosDeletionServiceDataStoreFactory(IOwnedScopeFactory<IFhirDataStore> dataStore)
        {
            _dataStore = EnsureArg.IsNotNull(dataStore, nameof(dataStore));
        }

        public DeletionServiceScopedDataStore GetScopedDataStore()
        {
            return new DeletionServiceScopedDataStore(_dataStore);
        }
    }
}
