// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.CosmosDb.Configs;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Versioning;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public class CollectionInitializer : ICollectionInitializer
    {
        private readonly string _collectionId;
        private readonly CosmosDataStoreConfiguration _cosmosDataStoreConfiguration;
        private readonly int? _initialCollectionThroughput;
        private readonly bool _autoscaleThroughput;
        private readonly IUpgradeManager _upgradeManager;
        private readonly ILogger<CollectionInitializer> _logger;

        public CollectionInitializer(string collectionId, CosmosDataStoreConfiguration cosmosDataStoreConfiguration, int? initialCollectionThroughput, bool autoscaleThroughput, IUpgradeManager upgradeManager, ILogger<CollectionInitializer> logger)
        {
            EnsureArg.IsNotNull(collectionId, nameof(collectionId));
            EnsureArg.IsNotNull(cosmosDataStoreConfiguration, nameof(cosmosDataStoreConfiguration));
            EnsureArg.IsNotNull(upgradeManager, nameof(upgradeManager));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _collectionId = collectionId;
            _cosmosDataStoreConfiguration = cosmosDataStoreConfiguration;
            _initialCollectionThroughput = initialCollectionThroughput;
            _autoscaleThroughput = autoscaleThroughput;
            _upgradeManager = upgradeManager;
            _logger = logger;
        }

        public async Task<Container> InitializeCollection(CosmosClient client)
        {
            Database database = client.GetDatabase(_cosmosDataStoreConfiguration.DatabaseId);
            Container containerClient = database.GetContainer(_collectionId);

            _logger.LogInformation("Finding Container: {collectionId}", _collectionId);
            var existingContainer = await database.TryGetContainerAsync(_collectionId);

            if (existingContainer == null)
            {
                _logger.LogInformation("Creating Cosmos Container if not exits: {collectionId}", _collectionId);

                ThroughputProperties throughputProperties = null;

                if (_initialCollectionThroughput.HasValue && _autoscaleThroughput)
                {
                    throughputProperties = ThroughputProperties.CreateAutoscaleThroughput(_initialCollectionThroughput.Value);
                }
                else if (_initialCollectionThroughput.HasValue)
                {
                    throughputProperties = ThroughputProperties.CreateManualThroughput(_initialCollectionThroughput.Value);
                }

                ContainerResponse containerResponse = await database.CreateContainerIfNotExistsAsync(
                    new ContainerProperties(_collectionId, $"/{KnownDocumentProperties.PartitionKey}"),
                    throughputProperties);

                containerResponse.Resource.DefaultTimeToLive = -1;

                existingContainer = await containerClient.ReplaceContainerAsync(containerResponse);
            }

            await _upgradeManager.SetupContainerAsync(containerClient);

            return existingContainer;
        }
    }
}
