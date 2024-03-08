// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Health.Fhir.CosmosDb.Core.Configs;
using Polly;

namespace Microsoft.Health.Fhir.CosmosDb.Initialization.Features.Storage
{
    public class NullcollectionSetup : ICollectionSetup
    {
        public Task CreateCollection(CosmosClient client, IEnumerable<ICollectionInitializer> collectionInitializers, CosmosDataStoreConfiguration cosmosDataStoreConfiguration, AsyncPolicy retryPolicy, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task CreateDatabaseAsync(CosmosClient client, CosmosDataStoreConfiguration cosmosDataStoreConfiguration, AsyncPolicy retryPolicy, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task UpdateFhirCollectionSettings(Container container, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
