// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Fhir.CosmosDb.Configs;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Versioning;
using Polly;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public class CollectionInitializer : ICollectionInitializer
    {
        private readonly CosmosCollectionConfiguration _cosmosCollectionConfiguration;
        private readonly CosmosDataStoreConfiguration _cosmosDataStoreConfiguration;
        private readonly IUpgradeManager _upgradeManager;
        private readonly RetryExceptionPolicyFactory _retryExceptionPolicyFactory;
        private readonly ICosmosClientTestProvider _clientTestProvider;
        private readonly ILogger<CollectionInitializer> _logger;

        public CollectionInitializer(
            CosmosCollectionConfiguration cosmosCollectionConfiguration,
            CosmosDataStoreConfiguration cosmosDataStoreConfiguration,
            IUpgradeManager upgradeManager,
            RetryExceptionPolicyFactory retryExceptionPolicyFactory,
            ICosmosClientTestProvider clientTestProvider,
            ILogger<CollectionInitializer> logger)
        {
            EnsureArg.IsNotNull(cosmosCollectionConfiguration, nameof(cosmosCollectionConfiguration));
            EnsureArg.IsNotNull(cosmosCollectionConfiguration.CollectionId, nameof(CosmosCollectionConfiguration.CollectionId));
            EnsureArg.IsNotNull(clientTestProvider, nameof(clientTestProvider));
            EnsureArg.IsNotNull(cosmosDataStoreConfiguration, nameof(cosmosDataStoreConfiguration));
            EnsureArg.IsNotNull(upgradeManager, nameof(upgradeManager));
            EnsureArg.IsNotNull(retryExceptionPolicyFactory, nameof(retryExceptionPolicyFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _cosmosCollectionConfiguration = cosmosCollectionConfiguration;
            _cosmosDataStoreConfiguration = cosmosDataStoreConfiguration;
            _upgradeManager = upgradeManager;
            _retryExceptionPolicyFactory = retryExceptionPolicyFactory;
            _clientTestProvider = clientTestProvider;
            _logger = logger;
        }

        public async Task<Container> InitializeCollectionAsync(CosmosClient client, CancellationToken cancellationToken = default)
        {
            Database database = client.GetDatabase(_cosmosDataStoreConfiguration.DatabaseId);
            Container containerClient = database.GetContainer(_cosmosCollectionConfiguration.CollectionId);

            _logger.LogInformation("Finding Container: {CollectionId}", _cosmosCollectionConfiguration.CollectionId);

            AsyncPolicy retryPolicy = _retryExceptionPolicyFactory.RetryPolicy;

            var existingContainer = await retryPolicy.ExecuteAsync(async () => await database.TryGetContainerAsync(_cosmosCollectionConfiguration.CollectionId));

            _logger.LogInformation("Creating Cosmos Container if not exits: {CollectionId}", _cosmosCollectionConfiguration.CollectionId);

            ContainerResponse containerResponse = await retryPolicy.ExecuteAsync(async () =>
                await database.CreateContainerIfNotExistsAsync(
                    _cosmosCollectionConfiguration.CollectionId,
                    $"/{KnownDocumentProperties.PartitionKey}",
                    _cosmosCollectionConfiguration.InitialCollectionThroughput));

            if (containerResponse.StatusCode == HttpStatusCode.Created || containerResponse.Resource.DefaultTimeToLive != -1)
            {
                if (_cosmosCollectionConfiguration.InitialCollectionThroughput.HasValue)
                {
                    var throughputProperties = ThroughputProperties.CreateManualThroughput(_cosmosCollectionConfiguration.InitialCollectionThroughput.Value);
                    await retryPolicy.ExecuteAsync(async () => await containerClient.ReplaceThroughputAsync(throughputProperties));
                }

                containerResponse.Resource.DefaultTimeToLive = -1;
                existingContainer = await retryPolicy.ExecuteAsync(async () => await containerClient.ReplaceContainerAsync(containerResponse));
            }

            await retryPolicy.ExecuteAsync(async () =>
            {
                try
                {
                    await _clientTestProvider.PerformTestAsync(existingContainer, _cosmosDataStoreConfiguration, _cosmosCollectionConfiguration, cancellationToken);
                }
                catch (CosmosException e) when (e.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    // This is the very first interaction with the collection, and we might get this exception
                    // when it calls GetCachedContainerPropertiesAsync, which does not use our request handler.
                    throw new RequestRateExceededException(e.RetryAfter);
                }
            });

            await _upgradeManager.SetupContainerAsync(containerClient);

            return existingContainer;
        }
    }
}
