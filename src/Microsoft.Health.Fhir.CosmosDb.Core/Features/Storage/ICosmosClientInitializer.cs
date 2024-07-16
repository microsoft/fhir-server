﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Health.Fhir.CosmosDb.Core.Configs;

namespace Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage
{
    /// <summary>
    /// Provides methods for creating a CosmosClient instance and initializing a collection.
    /// </summary>
    public interface ICosmosClientInitializer
    {
        /// <summary>
        /// Creates am unopened <see cref="CosmosClient"/> based on the given <see cref="CosmosDataStoreConfiguration"/>.
        /// </summary>
        /// <param name="configuration">The endpoint and collection settings</param>
        /// <returns>A <see cref="CosmosClient"/> instance</returns>
        CosmosClient CreateCosmosClient(CosmosDataStoreConfiguration configuration);

        /// <summary>
        /// Perform a trivial query to establish a connection.
        /// CosmosClient.OpenAsync() is not supported when a token is used as the access key.
        /// </summary>
        /// <param name="client">The document client</param>
        /// <param name="configuration">The data store config</param>
        /// <param name="cosmosCollectionConfiguration">The collection configuration for the query to use</param>
        Task OpenCosmosClient(CosmosClient client, CosmosDataStoreConfiguration configuration, CosmosCollectionConfiguration cosmosCollectionConfiguration);

        /// <summary>
        /// Creates a new Container instance for access the Cosmos API
        /// </summary>
        /// <param name="client">The Cosmos Client</param>
        /// <param name="databaseId">The database Id</param>
        /// <param name="collectionId">The collection Id</param>
        /// <returns>A <see cref="Container"/> instance</returns>
        Container CreateFhirContainer(CosmosClient client, string databaseId, string collectionId);
    }
}
