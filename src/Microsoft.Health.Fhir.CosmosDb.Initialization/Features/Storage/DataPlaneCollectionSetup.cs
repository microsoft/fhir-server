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
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.CosmosDb.Core;
using Microsoft.Health.Fhir.CosmosDb.Core.Configs;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage.Versioning;
using Polly;
using Polly.Retry;

namespace Microsoft.Health.Fhir.CosmosDb.Initialization.Features.Storage
{
    public class DataPlaneCollectionSetup : ICollectionSetup
    {
        private readonly ILogger<DataPlaneCollectionSetup> _logger;
        private const int CollectionSettingsVersion = 3;

        public DataPlaneCollectionSetup(ILogger<DataPlaneCollectionSetup> logger)
        {
            EnsureArg.IsNotNull(logger, nameof(logger));
            _logger = logger;
        }

        public async Task CreateDatabaseAsync(CosmosClient client, CosmosDataStoreConfiguration cosmosDataStoreConfiguration, AsyncPolicy retryPolicy, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(client, nameof(client));
            EnsureArg.IsNotNull(cosmosDataStoreConfiguration, nameof(cosmosDataStoreConfiguration));

            try
            {
                _logger.LogInformation("Creating Cosmos DB Database {DatabaseId}", cosmosDataStoreConfiguration.DatabaseId);

                if (cosmosDataStoreConfiguration.AllowDatabaseCreation)
                {
                    _logger.LogInformation("CreateDatabaseIfNotExists {DatabaseId}", cosmosDataStoreConfiguration.DatabaseId);

                    await retryPolicy.ExecuteAsync(
                        async () =>
                            await client.CreateDatabaseIfNotExistsAsync(
                                cosmosDataStoreConfiguration.DatabaseId,
                                cosmosDataStoreConfiguration.InitialDatabaseThroughput.HasValue ? ThroughputProperties.CreateManualThroughput(cosmosDataStoreConfiguration.InitialDatabaseThroughput.Value) : null,
                                cancellationToken: cancellationToken));
                }

                _logger.LogInformation("Cosmos DB Database {DatabaseId} successfully initialized", cosmosDataStoreConfiguration.DatabaseId);
            }
            catch (Exception ex)
            {
                LogLevel logLevel = ex is RequestRateExceededException ? LogLevel.Warning : LogLevel.Critical;
                _logger.Log(logLevel, ex, "Cosmos DB Database {DatabaseId} initialization failed", cosmosDataStoreConfiguration.DatabaseId);
                throw;
            }
        }

        public async Task CreateCollection(CosmosClient client, IEnumerable<ICollectionInitializer> collectionInitializers, CosmosDataStoreConfiguration cosmosDataStoreConfiguration, AsyncPolicy retryPolicy, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(client, nameof(client));
            EnsureArg.IsNotNull(cosmosDataStoreConfiguration, nameof(cosmosDataStoreConfiguration));
            EnsureArg.IsNotNull(collectionInitializers, nameof(collectionInitializers));

            try
            {
                _logger.LogInformation("Initializing Cosmos DB Database {DatabaseId} and collections", cosmosDataStoreConfiguration.DatabaseId);

                foreach (var collectionInitializer in collectionInitializers)
                {
                    await collectionInitializer.InitializeCollectionAsync(client, retryPolicy, cancellationToken);
                }

                _logger.LogInformation("Collections successfully initialized");
            }
            catch (Exception ex)
            {
                LogLevel logLevel = ex is RequestRateExceededException ? LogLevel.Warning : LogLevel.Critical;
                _logger.Log(logLevel, ex, "Collections initialization failed");
                throw;
            }
        }

        public async Task UpdateFhirCollectionSettings(Container container, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(container, nameof(container));

            var thisVersion = await GetLatestCollectionVersion(container, cancellationToken);

            if (thisVersion.Version < 2)
            {
                var containerResponse = await container.ReadContainerAsync(cancellationToken: cancellationToken);

                // For more information on setting indexing policies refer to:
                // https://docs.microsoft.com/en-us/azure/cosmos-db/how-to-manage-indexing-policy
                // It is no longer necessary to explicitly set the kind (range/hash)
                containerResponse.Resource.IndexingPolicy.IncludedPaths.Clear();
                containerResponse.Resource.IndexingPolicy.IncludedPaths.Add(new IncludedPath
                {
                    Path = "/*",
                });

                containerResponse.Resource.IndexingPolicy.ExcludedPaths.Clear();
                containerResponse.Resource.IndexingPolicy.ExcludedPaths.Add(new ExcludedPath { Path = $"/{Constants.RawResource}/*", });
                containerResponse.Resource.IndexingPolicy.ExcludedPaths.Add(new ExcludedPath { Path = $"/\"_etag\"/?", });

                // Setting the DefaultTTL to -1 means that by default all documents in the collection will live forever
                // but the Cosmos DB service should monitor this collection for documents that have overridden this default.
                // See: https://docs.microsoft.com/en-us/azure/cosmos-db/time-to-live
                containerResponse.Resource.DefaultTimeToLive = -1;

                await container.ReplaceContainerAsync(containerResponse, cancellationToken: cancellationToken);

                thisVersion.Version = CollectionSettingsVersion;
                await container.UpsertItemAsync(thisVersion, new PartitionKey(thisVersion.PartitionKey), cancellationToken: cancellationToken);
            }

            // TODO:Need to Validate this logic
           /* else if (thisVersion.Version < CollectionSettingsVersion)
            {
                await _updateSP.Execute(container.Scripts, cancellationToken);
                thisVersion.Version = CollectionSettingsVersion;
                await container.UpsertItemAsync(thisVersion, new PartitionKey(thisVersion.PartitionKey), cancellationToken: cancellationToken);
            } */
        }

        private static async Task<CollectionVersion> GetLatestCollectionVersion(Container container, CancellationToken cancellationToken)
        {
            FeedIterator<CollectionVersion> query = container.GetItemQueryIterator<CollectionVersion>(
                new QueryDefinition("SELECT * FROM root r"),
                requestOptions: new QueryRequestOptions
                {
                    PartitionKey = new PartitionKey(CollectionVersion.CollectionVersionPartition),
                });

            FeedResponse<CollectionVersion> result = await query.ReadNextAsync(cancellationToken);

            return result.FirstOrDefault() ?? new CollectionVersion();
        }
    }
}
