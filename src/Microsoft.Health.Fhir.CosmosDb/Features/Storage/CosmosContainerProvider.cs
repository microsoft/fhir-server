// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Medino;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Core;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Messages.Storage;
using Microsoft.Health.Fhir.CosmosDb.Core.Configs;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage.Versioning;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    /// <summary>
    /// Provides an <see cref="Container"/> instance that is opened and whose collection has been properly initialized for use.
    /// Initialization starts asynchronously during application startup and is guaranteed to complete before any web request is handled by a controller.
    /// </summary>
    public class CosmosContainerProvider : IHostedService, IRequireInitializationOnFirstRequest, IDisposable
    {
        private const int CollectionSettingsVersion = 3;
        private readonly ILogger<CosmosContainerProvider> _logger;
        private readonly IMediator _mediator;
        private readonly ICosmosDbDistributedLockFactory _distributedLockFactory;
        private Lazy<Container> _container;
        private readonly RetryableInitializationOperation _initializationOperation;
        private readonly CosmosClient _client;
        private readonly Func<CancellationToken, Task> _containerTestFactory;

        public CosmosContainerProvider(
            CosmosDataStoreConfiguration cosmosDataStoreConfiguration,
            IOptionsMonitor<CosmosCollectionConfiguration> collectionConfiguration,
            ICosmosClientInitializer cosmosClientInitializer,
            ICollectionSetup collectionSetup,
            ICollectionDataUpdater collectionDataUpdater,
            RetryExceptionPolicyFactory retryPolicyFactory,
            ILogger<CosmosContainerProvider> logger,
            IMediator mediator,
            IEnumerable<ICollectionInitializer> collectionInitializers,
            ICosmosDbDistributedLockFactory distributedLockFactory,
            ICosmosClientTestProvider cosmosClientTestProvider)
        {
            EnsureArg.IsNotNull(cosmosDataStoreConfiguration, nameof(cosmosDataStoreConfiguration));
            EnsureArg.IsNotNull(collectionConfiguration, nameof(collectionConfiguration));
            EnsureArg.IsNotNull(cosmosClientInitializer, nameof(cosmosClientInitializer));
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(collectionInitializers, nameof(collectionInitializers));
            EnsureArg.IsNotNull(collectionSetup, nameof(collectionSetup));
            EnsureArg.IsNotNull(collectionDataUpdater, nameof(collectionDataUpdater));
            EnsureArg.IsNotNull(retryPolicyFactory, nameof(retryPolicyFactory));
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(distributedLockFactory, nameof(distributedLockFactory));

            _logger = logger;
            _mediator = mediator;
            _distributedLockFactory = distributedLockFactory;

            string collectionId = collectionConfiguration.Get(Constants.CollectionConfigurationName).CollectionId;
            _client = cosmosClientInitializer.CreateCosmosClient(cosmosDataStoreConfiguration);

            Func<Container> initializationFactory = () => cosmosClientInitializer.CreateFhirContainer(
                _client,
                cosmosDataStoreConfiguration.DatabaseId,
                collectionId);

            _container = new Lazy<Container>(initializationFactory);

            _containerTestFactory = ct => cosmosClientTestProvider.PerformTestAsync(
                initializationFactory.Invoke(),
                ct);

            _initializationOperation = new RetryableInitializationOperation(async () =>
            {
                await InitializeDataStoreAsync(collectionSetup, collectionDataUpdater, cosmosDataStoreConfiguration, retryPolicyFactory, collectionInitializers);
            });
        }

        private Container Container
        {
            get
            {
                if (!_initializationOperation.IsInitialized)
                {
#pragma warning disable CA1065
                    throw new InvalidOperationException($"{nameof(CosmosContainerProvider)} has not been initialized.");
#pragma warning restore CA1065
                }

                return _container.Value;
            }
        }

        private async Task InitializeDataStoreAsync(
            ICollectionSetup collectionSetup,
            ICollectionDataUpdater collectionDataUpdater,
            CosmosDataStoreConfiguration cosmosDataStoreConfiguration,
            RetryExceptionPolicyFactory retryPolicyFactory,
            IEnumerable<ICollectionInitializer> collectionInitializers)
        {
            try
            {
                using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMinutes(5));

                _logger.LogInformation("Initializing Cosmos DB Database {DatabaseId} and collections", cosmosDataStoreConfiguration.DatabaseId);

                try
                {
                    await _containerTestFactory.Invoke(cancellationTokenSource.Token);
                }
                catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogInformation("The database or collection does not exist, setup is required.");

                    if (cosmosDataStoreConfiguration.AllowDatabaseCreation)
                    {
                        await collectionSetup.CreateDatabaseAsync(retryPolicyFactory.RetryPolicy, cancellationTokenSource.Token);
                    }

                    if (cosmosDataStoreConfiguration.AllowCollectionSetup)
                    {
                        await collectionSetup.CreateCollectionAsync(collectionInitializers, retryPolicyFactory.RetryPolicy, cancellationTokenSource.Token);
                    }
                }

                // When the collection exists we can start a distributed lock to ensure only one instance of the service does the rest of the setup
                ICosmosDbDistributedLock setupLock = _distributedLockFactory.Create(_container.Value, nameof(InitializeDataStoreAsync));

                await setupLock.AcquireLock(cancellationTokenSource.Token);
                try
                {
                    if (cosmosDataStoreConfiguration.AllowCollectionSetup)
                    {
                        await collectionSetup.InstallStoredProcs(cancellationTokenSource.Token);

                        (bool updateRequired, CollectionVersion version) = await IsUpdateRequiredAsync(_container.Value, cancellationTokenSource.Token);
                        if (updateRequired)
                        {
                            await collectionSetup.UpdateFhirCollectionSettingsAsync(version, cancellationTokenSource.Token);
                            await SaveCollectionVersion(_container.Value, version, cancellationTokenSource.Token);
                        }
                    }

                    await collectionDataUpdater.ExecuteAsync(_container.Value, cancellationTokenSource.Token);
                }
                finally
                {
                    await setupLock.ReleaseLock();
                }
            }
            catch (Exception ex)
            {
                LogLevel logLevel = LogLevel.Critical;
                _logger.Log(logLevel, ex, "Cosmos DB Database {DatabaseId} Initialization has failed.", cosmosDataStoreConfiguration.DatabaseId);
                throw;
            }
        }

        /// <summary>
        /// Starts the initialization of the document client and cosmos data store.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        public Task StartAsync(CancellationToken cancellationToken)
        {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            // The result is ignored and will be awaited in EnsureInitialized(). Exceptions are logged within CosmosClientInitializer.
            _initializationOperation.EnsureInitialized()
                .AsTask()
                .ContinueWith(_ => _mediator.PublishAsync(new StorageInitializedNotification(), CancellationToken.None), TaskScheduler.Default);
#pragma warning restore CS4014

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        /// <summary>
        /// Returns a task representing the initialization operation. Once completed,
        /// this method will always return a completed task. If the task fails, the method
        /// can be called again to retry the operation.
        /// </summary>
        /// <returns>A task representing the initialization operation.</returns>
        public async Task EnsureInitialized() => await _initializationOperation.EnsureInitialized();

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _initializationOperation.Dispose();
                _client.Dispose();
                _container = null;
            }
        }

        public IScoped<Container> CreateContainerScope()
        {
            if (!_initializationOperation.IsInitialized)
            {
                try
                {
                    _initializationOperation.EnsureInitialized().AsTask().GetAwaiter().GetResult();
                }
                catch (Exception ex) when (ex is not RequestRateExceededException)
                {
                    _logger.LogCritical(ex, "Couldn't create a ContainerScope because EnsureInitialized failed.");
                    throw new ServiceUnavailableException();
                }
            }

            return new NonDisposingScope(Container);
        }

        private static async Task<(bool UpdateRequired, CollectionVersion Version)> IsUpdateRequiredAsync(Container container, CancellationToken cancellationToken)
        {
            CollectionVersion thisVersion = await GetLatestCollectionVersion(container, cancellationToken);
            return (thisVersion.Version < CollectionSettingsVersion, thisVersion);
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

        private static async Task SaveCollectionVersion(Container container, CollectionVersion collectionVersion, CancellationToken cancellationToken)
        {
            collectionVersion.Version = CollectionSettingsVersion;
            await container.UpsertItemAsync(collectionVersion, new PartitionKey(collectionVersion.PartitionKey), cancellationToken: cancellationToken);
        }
    }
}
