// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Fhir.CosmosDb.Core;
using Microsoft.Health.Fhir.CosmosDb.Core.Configs;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage.StoredProcedures;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage.Versioning;
using Polly;

namespace Microsoft.Health.Fhir.CosmosDb.Initialization.Features.Storage
{
    public class DataPlaneCollectionSetup : ICollectionSetup
    {
        private readonly ILogger<DataPlaneCollectionSetup> _logger;
        private readonly CosmosClient _client;
        private readonly Lazy<Container> _container;
        private readonly CosmosDataStoreConfiguration _cosmosDataStoreConfiguration;
        private readonly IStoredProcedureInstaller _storedProcedureInstaller;

        public DataPlaneCollectionSetup(
           CosmosDataStoreConfiguration cosmosDataStoreConfiguration,
           IOptionsMonitor<CosmosCollectionConfiguration> collectionConfiguration,
           ICosmosClientInitializer cosmosClientInitializer,
           IStoredProcedureInstaller storedProcedureInstaller,
           ILogger<DataPlaneCollectionSetup> logger)
        {
            EnsureArg.IsNotNull(cosmosDataStoreConfiguration, nameof(cosmosDataStoreConfiguration));
            EnsureArg.IsNotNull(cosmosClientInitializer, nameof(cosmosClientInitializer));
            EnsureArg.IsNotNull(collectionConfiguration, nameof(collectionConfiguration));
            EnsureArg.IsNotNull(storedProcedureInstaller, nameof(storedProcedureInstaller));
            EnsureArg.IsNotNull(logger, nameof(logger));

            string collectionId = collectionConfiguration.Get(Constants.CollectionConfigurationName).CollectionId;
            _cosmosDataStoreConfiguration = cosmosDataStoreConfiguration;
            _client = cosmosClientInitializer.CreateCosmosClient(cosmosDataStoreConfiguration);
            _container = new Lazy<Container>(() => cosmosClientInitializer.CreateFhirContainer(
                _client,
                cosmosDataStoreConfiguration.DatabaseId,
                collectionId));
            _storedProcedureInstaller = storedProcedureInstaller;
            _logger = logger;
        }

        public async Task CreateDatabaseAsync(AsyncPolicy retryPolicy, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(retryPolicy, nameof(retryPolicy));

            try
            {
                _logger.LogInformation("Creating Cosmos DB Database {DatabaseId}", _cosmosDataStoreConfiguration.DatabaseId);

                if (_cosmosDataStoreConfiguration.AllowDatabaseCreation)
                {
                    _logger.LogInformation("CreateDatabaseIfNotExists {DatabaseId}", _cosmosDataStoreConfiguration.DatabaseId);

                    await retryPolicy.ExecuteAsync(
                        async () =>
                            await _client.CreateDatabaseIfNotExistsAsync(
                                _cosmosDataStoreConfiguration.DatabaseId,
                                _cosmosDataStoreConfiguration.InitialDatabaseThroughput.HasValue ? ThroughputProperties.CreateManualThroughput(_cosmosDataStoreConfiguration.InitialDatabaseThroughput.Value) : null,
                                cancellationToken: cancellationToken));
                }

                _logger.LogInformation("Cosmos DB Database {DatabaseId} successfully initialized", _cosmosDataStoreConfiguration.DatabaseId);
            }
            catch (Exception ex)
            {
                LogLevel logLevel = ex is RequestRateExceededException ? LogLevel.Warning : LogLevel.Critical;
                _logger.Log(logLevel, ex, "Cosmos DB Database {DatabaseId} initialization failed", _cosmosDataStoreConfiguration.DatabaseId);
                throw;
            }
        }

        public async Task CreateCollectionAsync(IEnumerable<ICollectionInitializer> collectionInitializers, AsyncPolicy retryPolicy, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(_client, nameof(_client));
            EnsureArg.IsNotNull(_cosmosDataStoreConfiguration, nameof(_cosmosDataStoreConfiguration));
            EnsureArg.IsNotNull(collectionInitializers, nameof(collectionInitializers));
            EnsureArg.IsNotNull(retryPolicy, nameof(retryPolicy));

            try
            {
                _logger.LogInformation("Initializing Cosmos DB Database {DatabaseId} and collections", _cosmosDataStoreConfiguration.DatabaseId);

                foreach (var collectionInitializer in collectionInitializers)
                {
                    await collectionInitializer.InitializeCollectionAsync(_client, retryPolicy, cancellationToken);
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

        public async Task InstallStoredProcs(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Installing Stored Procedures");

            await _storedProcedureInstaller.ExecuteAsync(_container.Value, cancellationToken);

            _logger.LogInformation("Stored Procedures are installed");
        }

        public async Task UpdateFhirCollectionSettingsAsync(CollectionVersion version, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(_container, nameof(_container));

            if (version.Version < 2)
            {
                var containerResponse = await _container.Value.ReadContainerAsync(cancellationToken: cancellationToken);

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

                await _container.Value.ReplaceContainerAsync(containerResponse, cancellationToken: cancellationToken);
            }
        }
    }
}
