// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.CosmosDb.Configs;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Versioning
{
    /// <summary>
    /// Updates a document collection to the latest index
    /// </summary>
    public sealed class FhirCollectionSettingsUpdater : ICollectionUpdater
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

        public async Task ExecuteAsync(Container container, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(container, nameof(container));

            if (_configuration.UseManagedIdentity)
            {
                // Managed Identity does not support updating these settings
                _logger.LogInformation("Skipping modifying collection settings because managed identity is enabled");
                return;
            }

            var thisVersion = await GetLatestCollectionVersion(container, cancellationToken);

            if (thisVersion.Version < CollectionSettingsVersion)
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
                containerResponse.Resource.IndexingPolicy.ExcludedPaths.Add(new ExcludedPath { Path = $"/{KnownResourceWrapperProperties.RawResource}/*", });
                containerResponse.Resource.IndexingPolicy.ExcludedPaths.Add(new ExcludedPath { Path = $"/\"_etag\"/?", });

                // Setting the DefaultTTL to -1 means that by default all documents in the collection will live forever
                // but the Cosmos DB service should monitor this collection for documents that have overridden this default.
                // See: https://docs.microsoft.com/en-us/azure/cosmos-db/time-to-live
                containerResponse.Resource.DefaultTimeToLive = -1;

                await container.ReplaceContainerAsync(containerResponse, cancellationToken: cancellationToken);

                thisVersion.Version = CollectionSettingsVersion;
                await container.UpsertItemAsync(thisVersion, new PartitionKey(thisVersion.PartitionKey), cancellationToken: cancellationToken);
            }
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
