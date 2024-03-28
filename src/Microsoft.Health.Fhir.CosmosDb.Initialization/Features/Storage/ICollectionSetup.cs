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
    public interface ICollectionSetup
    {
        public Task CreateDatabaseAsync(CosmosClient client, CosmosDataStoreConfiguration cosmosDataStoreConfiguration, AsyncPolicy retryPolicy, CancellationToken cancellationToken);

        public Task CreateCollectionAsync(CosmosClient client, IEnumerable<ICollectionInitializer> collectionInitializers, CosmosDataStoreConfiguration cosmosDataStoreConfiguration, AsyncPolicy retryPolicy, CancellationToken cancellationToken = default);

        public Task UpdateFhirCollectionSettingsAsync(Container container, CancellationToken cancellationToken);
    }
}
