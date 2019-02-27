// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Linq;
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

        public async Task ExecuteAsync(IDocumentClient client, DocumentCollection collection, Uri relativeCollectionUri)
        {
            EnsureArg.IsNotNull(client, nameof(client));
            EnsureArg.IsNotNull(collection, nameof(collection));

            var thisVersion = await GetLatestCollectionVersion(client, collection);

            if (thisVersion.Version < CollectionSettingsVersion)
            {
                _logger.LogDebug("Ensuring indexes are up-to-date {CollectionUri}", _configuration.GetAbsoluteCollectionUri(_collectionConfiguration.CollectionId));

                collection.IndexingPolicy = new IndexingPolicy
                {
                    IncludedPaths = new Collection<IncludedPath>
                    {
                        new IncludedPath
                        {
                            Path = "/*",
                            Indexes = new Collection<Index>
                            {
                                new RangeIndex(DataType.Number, -1),
                                new RangeIndex(DataType.String, -1),
                            },
                        },
                    },
                    ExcludedPaths = new Collection<ExcludedPath>
                    {
                        new ExcludedPath
                        {
                            Path = $"/{KnownResourceWrapperProperties.RawResource}/*",
                        },
                    },
                };

                // Setting the DefaultTTL to -1 means that by default all documents in the collection will live forever
                // but the Cosmos DB service should monitor this collection for documents that have overridden this default.
                // See: https://docs.microsoft.com/en-us/azure/cosmos-db/time-to-live
                collection.DefaultTimeToLive = -1;

                await client.ReplaceDocumentCollectionAsync(collection);

                thisVersion.Version = CollectionSettingsVersion;
                await client.UpsertDocumentAsync(relativeCollectionUri, thisVersion);
            }
        }

        private static async Task<CollectionVersion> GetLatestCollectionVersion(IDocumentClient documentClient, DocumentCollection collection)
        {
            var query = documentClient.CreateDocumentQuery<CollectionVersion>(
                    collection.SelfLink,
                    new SqlQuerySpec("SELECT * FROM root r"),
                    new FeedOptions { PartitionKey = new PartitionKey(CollectionVersion.CollectionVersionPartition) })
                .AsDocumentQuery();

            var result = await query.ExecuteNextAsync<CollectionVersion>();

            return result.FirstOrDefault() ?? new CollectionVersion();
        }
    }
}
