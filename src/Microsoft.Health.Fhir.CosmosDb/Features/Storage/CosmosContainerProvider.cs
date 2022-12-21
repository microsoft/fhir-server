// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Core;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Messages.Storage;
using Microsoft.Health.Fhir.CosmosDb.Configs;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    /// <summary>
    /// Provides an <see cref="Container"/> instance that is opened and whose collection has been properly initialized for use.
    /// Initialization starts asynchronously during application startup and is guaranteed to complete before any web request is handled by a controller.
    /// </summary>
    public class CosmosContainerProvider : IHostedService, IRequireInitializationOnFirstRequest, IDisposable
    {
        private readonly ILogger<CosmosContainerProvider> _logger;
        private readonly IMediator _mediator;
        private Lazy<Container> _container;
        private readonly RetryableInitializationOperation _initializationOperation;
        private readonly CosmosClient _client;

        public CosmosContainerProvider(
            CosmosDataStoreConfiguration cosmosDataStoreConfiguration,
            IOptionsMonitor<CosmosCollectionConfiguration> collectionConfiguration,
            ICosmosClientInitializer cosmosClientInitializer,
            ILogger<CosmosContainerProvider> logger,
            IMediator mediator,
            IEnumerable<ICollectionInitializer> collectionInitializers)
        {
            EnsureArg.IsNotNull(cosmosDataStoreConfiguration, nameof(cosmosDataStoreConfiguration));
            EnsureArg.IsNotNull(collectionConfiguration, nameof(collectionConfiguration));
            EnsureArg.IsNotNull(cosmosClientInitializer, nameof(cosmosClientInitializer));
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(collectionInitializers, nameof(collectionInitializers));
            _logger = logger;
            _mediator = mediator;

            string collectionId = collectionConfiguration.Get(Constants.CollectionConfigurationName).CollectionId;
            _client = cosmosClientInitializer.CreateCosmosClient(cosmosDataStoreConfiguration);

            _initializationOperation = new RetryableInitializationOperation(
                () => cosmosClientInitializer.InitializeDataStoreAsync(_client, cosmosDataStoreConfiguration, collectionInitializers));

            _container = new Lazy<Container>(() => cosmosClientInitializer.CreateFhirContainer(
                _client,
                cosmosDataStoreConfiguration.DatabaseId,
                collectionId));
        }

        public Container Container
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
                .ContinueWith(_ => _mediator.Publish(new StorageInitializedNotification(), CancellationToken.None), TaskScheduler.Default);
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
    }
}
