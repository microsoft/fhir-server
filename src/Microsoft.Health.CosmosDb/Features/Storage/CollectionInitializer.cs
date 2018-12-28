// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Health.CosmosDb.Configs;
using Microsoft.Health.CosmosDb.Features.Storage.Versioning;

namespace Microsoft.Health.CosmosDb.Features.Storage
{
    public class CollectionInitializer : ICollectionInitializer
    {
        private readonly string _collectionId;
        private readonly Uri _relativeDatabaseUri;
        private readonly Uri _relativeCollectionUri;
        private readonly int? _initialCollectionThroughput;
        private readonly IUpgradeManager _upgradeManager;
        private readonly ILogger<CollectionInitializer> _logger;

        public CollectionInitializer(string collectionId, CosmosDataStoreConfiguration cosmosDataStoreConfiguration, int? initialCollectionThroughput, IUpgradeManager upgradeManager, ILogger<CollectionInitializer> logger)
        {
            EnsureArg.IsNotNull(collectionId, nameof(collectionId));
            EnsureArg.IsNotNull(cosmosDataStoreConfiguration, nameof(cosmosDataStoreConfiguration));
            EnsureArg.IsNotNull(upgradeManager, nameof(upgradeManager));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _collectionId = collectionId;
            _relativeDatabaseUri = cosmosDataStoreConfiguration.RelativeDatabaseUri;
            _relativeCollectionUri = cosmosDataStoreConfiguration.GetRelativeCollectionUri(collectionId);
            _initialCollectionThroughput = initialCollectionThroughput;
            _upgradeManager = upgradeManager;
            _logger = logger;
        }

        public async Task<DocumentCollection> InitializeCollection(IDocumentClient documentClient)
        {
            DocumentCollection existingDocumentCollection = await documentClient.TryGetDocumentCollectionAsync(_relativeCollectionUri);

            if (existingDocumentCollection == null)
            {
                _logger.LogDebug("Creating document collection if not exits: {collectionId}", _collectionId);
                var documentCollection = new DocumentCollection
                {
                    Id = _collectionId,
                    PartitionKey = new PartitionKeyDefinition
                    {
                        Paths =
                        {
                            $"/{KnownDocumentProperties.PartitionKey}",
                        },
                    },
                };

                var requestOptions = new RequestOptions { OfferThroughput = _initialCollectionThroughput };

                existingDocumentCollection = await documentClient.CreateDocumentCollectionIfNotExistsAsync(
                    _relativeDatabaseUri, _relativeCollectionUri, documentCollection, requestOptions);
            }

            await _upgradeManager.SetupCollectionAsync(documentClient, existingDocumentCollection);

            return existingDocumentCollection;
        }
    }
}
