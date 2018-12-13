// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

////using System;
////using System.Collections.ObjectModel;
////using System.Linq;
////using System.Threading.Tasks;
////using EnsureThat;
////using Microsoft.Azure.Documents;
////using Microsoft.Azure.Documents.Client;
////using Microsoft.Azure.Documents.Linq;
////using Microsoft.Extensions.Logging;
////using Microsoft.Health.CosmosDb.Configs;

////namespace Microsoft.Health.CosmosDb.Features.Storage.Versioning
////{
////    /// <summary>
////    /// Updates a document collection to the latest index
////    /// </summary>
////    public sealed class CollectionSettingsUpdater : ICollectionUpdater
////    {
////        private readonly ILogger<CollectionSettingsUpdater> _logger;
////        private readonly CosmosDataStoreConfiguration _configuration;
////        private static readonly RangeIndex DefaultStringRangeIndex = new RangeIndex(DataType.String, -1);

////        private const int CollectionSettingsVersion = 1;

////        public CollectionSettingsUpdater(ILogger<CollectionSettingsUpdater> logger, CosmosDataStoreConfiguration configuration)
////        {
////            EnsureArg.IsNotNull(logger, nameof(logger));
////            EnsureArg.IsNotNull(configuration, nameof(configuration));

////            _logger = logger;
////            _configuration = configuration;
////        }

////        public async Task ExecuteAsync(IDocumentClient client, DocumentCollection collection, Uri relativeCollectionUri)
////        {
////            EnsureArg.IsNotNull(client, nameof(client));
////            EnsureArg.IsNotNull(collection, nameof(collection));

////            var thisVersion = await GetLatestCollectionVersion(client, collection);

////            if (thisVersion.Version < CollectionSettingsVersion)
////            {
////                _logger.LogDebug("Ensuring indexes are up-to-date {CollectionUri}", _configuration.AbsoluteFhirCollectionUri);

////                collection.IndexingPolicy = new IndexingPolicy
////                {
////                    IncludedPaths = new Collection<IncludedPath>
////                {
////                    new IncludedPath
////                    {
////                        Path = "/*",
////                        Indexes = new Collection<Index>
////                        {
////                            new RangeIndex(DataType.Number, -1),
////                            new HashIndex(DataType.String, 3),
////                        },
////                    },
////                    new IncludedPath
////                    {
////                        ////Path = $"/{KnownResourceWrapperProperties.LastModified}/?", // TODO: FIX THIS
////                        Indexes = new Collection<Index>
////                        {
////                            DefaultStringRangeIndex,
////                        },
////                    },
////                    ////GenerateIncludedPathForSearchIndexEntryField(SearchValueConstants.DateTimeStartName, DefaultStringRangeIndex), // TODO: FIX THIS
////                    ////GenerateIncludedPathForSearchIndexEntryField(SearchValueConstants.DateTimeEndName, DefaultStringRangeIndex), // TODO: FIX THIS
////                },
////                    ExcludedPaths = new Collection<ExcludedPath>
////                {
////                    new ExcludedPath
////                    {
////                        ////Path = $"/{KnownResourceWrapperProperties.RawResource}/*", // TODO: FIX THIS
////                    },
////                },
////                };

////                // Setting the DefaultTTL to -1 means that by default all documents in the collection will live forever
////                // but the Cosmos DB service should monitor this collection for documents that have overridden this default.
////                // See: https://docs.microsoft.com/en-us/azure/cosmos-db/time-to-live
////                collection.DefaultTimeToLive = -1;

////                await client.ReplaceDocumentCollectionAsync(collection);

////                thisVersion.Version = CollectionSettingsVersion;
////                await client.UpsertDocumentAsync(relativeCollectionUri, thisVersion);
////            }
////        }

////        private static async Task<CollectionVersion> GetLatestCollectionVersion(IDocumentClient documentClient, DocumentCollection collection)
////        {
////            var query = documentClient.CreateDocumentQuery<CollectionVersion>(
////                    collection.SelfLink,
////                    new SqlQuerySpec("SELECT * FROM root r"),
////                    new FeedOptions { PartitionKey = new PartitionKey(CollectionVersion.CollectionVersionPartition) })
////                .AsDocumentQuery();

////            var result = await query.ExecuteNextAsync<CollectionVersion>();

////            return result.FirstOrDefault() ?? new CollectionVersion();
////        }

////        private static IncludedPath GenerateIncludedPathForSearchIndexEntryField(string fieldName, params Index[] indices)
////        {
////            return new IncludedPath
////            {
////                Path = $"/SearchIndices/[]/{fieldName}/?", // TODO: FIX THIS
////                ////Path = $"/{KnownResourceWrapperProperties.SearchIndices}/[]/{fieldName}/?",// TODO: FIX THIS
////                Indexes = new Collection<Index>(indices),
////            };
////        }
////    }
////}
