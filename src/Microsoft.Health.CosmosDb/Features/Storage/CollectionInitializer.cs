// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Health.CosmosDb.Configs;
using Microsoft.Health.CosmosDb.Features.Storage.Versioning;

namespace Microsoft.Health.CosmosDb.Features.Storage
{
    public class CollectionInitializer : ICollectionInitializer
    {
        private readonly string _collectionId;
        private readonly CosmosDataStoreConfiguration _cosmosDataStoreConfiguration;
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
            _cosmosDataStoreConfiguration = cosmosDataStoreConfiguration;
            _initialCollectionThroughput = initialCollectionThroughput;
            _upgradeManager = upgradeManager;
            _logger = logger;
        }

        public async Task<Container> InitializeCollection(CosmosClient documentClient)
        {
            Database database = documentClient.GetDatabase(_cosmosDataStoreConfiguration.DatabaseId);
            Container containerClient = database.GetContainer(_collectionId);

            var existingDocumentCollection = await database.TryGetDocumentCollectionAsync(_collectionId);

            if (existingDocumentCollection == null)
            {
                _logger.LogDebug("Creating document collection if not exits: {collectionId}", _collectionId);

                var containerResponse = await database.CreateContainerIfNotExistsAsync(
                    _collectionId,
                    $"/{KnownDocumentProperties.PartitionKey}",
                    _initialCollectionThroughput);

                containerResponse.Resource.DefaultTimeToLive = -1;

                existingDocumentCollection = await database.GetContainer(_collectionId).ReplaceContainerAsync(containerResponse);

                if (_initialCollectionThroughput.HasValue)
                {
                    ThroughputProperties throughputProperties = ThroughputProperties.CreateManualThroughput(_initialCollectionThroughput.Value);
                    await containerClient.ReplaceThroughputAsync(throughputProperties);
                }
            }

            await _upgradeManager.SetupContainerAsync(containerClient);

            return existingDocumentCollection;
        }
    }
}
