// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.CosmosDb.Configs;
using Microsoft.Health.CosmosDb.Features.Storage.Versioning;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Versioning
{
    /// <summary>
    /// Updates a document collection to the latest index
    /// </summary>
    public sealed class FhirCollectionSettingsUpdater : IFhirCollectionUpdater
    {
        private readonly ILogger<FhirCollectionSettingsUpdater> _logger;
        private readonly CosmosDataStoreConfiguration _configuration;
        private readonly CosmosCollectionConfiguration _collectionConfiguration;

        private const int CollectionSettingsVersion = 2;

        public FhirCollectionSettingsUpdater(
            CosmosDataStoreConfiguration configuration,
            IOptionsMonitor<CosmosCollectionConfiguration> namedCosmosCollectionConfigurationAccessor,
            ILogger<FhirCollectionSettingsUpdater> logger)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));
            EnsureArg.IsNotNull(namedCosmosCollectionConfigurationAccessor, nameof(namedCosmosCollectionConfigurationAccessor));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _configuration = configuration;
            _collectionConfiguration = namedCosmosCollectionConfigurationAccessor.Get(Constants.CollectionConfigurationName);
            _logger = logger;
        }

        public async Task ExecuteAsync(Container client)
        {
            EnsureArg.IsNotNull(client, nameof(client));

            var thisVersion = await GetLatestCollectionVersion(client);

            if (thisVersion.Version < CollectionSettingsVersion)
            {
                _logger.LogDebug("Ensuring indexes are up-to-date {CollectionUri}");

                var container = await client.ReadContainerAsync();

                container.Resource.IndexingPolicy.IncludedPaths.Clear();
                container.Resource.IndexingPolicy.IncludedPaths.Add(new IncludedPath
                {
                    Path = "/*",
                });

                container.Resource.IndexingPolicy.ExcludedPaths.Clear();
                container.Resource.IndexingPolicy.ExcludedPaths.Add(new ExcludedPath
                {
                    Path = $"/{KnownResourceWrapperProperties.RawResource}/*",
                });

                // Setting the DefaultTTL to -1 means that by default all documents in the collection will live forever
                // but the Cosmos DB service should monitor this collection for documents that have overridden this default.
                // See: https://docs.microsoft.com/en-us/azure/cosmos-db/time-to-live
                container.Resource.DefaultTimeToLive = -1;

                await client.ReplaceContainerAsync(container);

                thisVersion.Version = CollectionSettingsVersion;
                await client.UpsertItemAsync(thisVersion, new PartitionKey(thisVersion.PartitionKey));
            }
        }

        private static async Task<CollectionVersion> GetLatestCollectionVersion(Container documentClient)
        {
            FeedIterator<CollectionVersion> query = documentClient.GetItemQueryIterator<CollectionVersion>(
                new QueryDefinition("SELECT * FROM root r"),
                requestOptions: new QueryRequestOptions
                {
                    PartitionKey = new PartitionKey(CollectionVersion.CollectionVersionPartition),
                });

            FeedResponse<CollectionVersion> result = await query.ReadNextAsync();

            return result.FirstOrDefault() ?? new CollectionVersion();
        }
    }
}
