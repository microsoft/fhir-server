// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Extensions.Logging;
using Microsoft.Health.CosmosDb.Configs;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core;

namespace Microsoft.Health.CosmosDb.Features.Storage
{
    /// <summary>
    /// Provides an <see cref="IDocumentClient"/> instance that is opened and whose collection has been properly initialized for use.
    /// Initialization starts asynchronously during application startup and is guaranteed to complete before any web request is handled by a controller.
    /// </summary>
    public class DocumentClientProvider : IStartable, IRequireInitializationOnFirstRequest, IDisposable
    {
        private IDocumentClient _documentClient;
        private readonly RetryableInitializationOperation _initializationOperation;

        public DocumentClientProvider(
            CosmosDataStoreConfiguration cosmosDataStoreConfiguration,
            IDocumentClientInitializer documentClientInitializer,
            ILogger<DocumentClientProvider> logger,
            IEnumerable<ICollectionInitializer> collectionInitializers)
        {
            EnsureArg.IsNotNull(cosmosDataStoreConfiguration, nameof(cosmosDataStoreConfiguration));
            EnsureArg.IsNotNull(documentClientInitializer, nameof(documentClientInitializer));
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(collectionInitializers, nameof(collectionInitializers));

            _documentClient = documentClientInitializer.CreateDocumentClient(cosmosDataStoreConfiguration);

            _initializationOperation = new RetryableInitializationOperation(
                () => documentClientInitializer.InitializeDataStore(_documentClient, cosmosDataStoreConfiguration, collectionInitializers));
        }

        public IDocumentClient DocumentClient
        {
            get
            {
                if (!_initializationOperation.IsInitialized)
                {
                    throw new InvalidOperationException($"{nameof(DocumentClientProvider)} has not been initialized.");
                }

                return _documentClient;
            }
        }

        /// <summary>
        /// Starts the initialization of the document client and cosmos data store.
        /// </summary>
        public void Start()
        {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            // The result is ignored and will be awaited in EnsureInitialized(). Exceptions are logged within DocumentClientInitializer.
            _initializationOperation.EnsureInitialized();
#pragma warning restore CS4014
        }

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
                _documentClient?.Dispose();
                _documentClient = null;
            }
        }

        public IScoped<IDocumentClient> CreateDocumentClientScope()
        {
            if (!_initializationOperation.IsInitialized)
            {
                _initializationOperation.EnsureInitialized().GetAwaiter().GetResult();
            }

            return new NonDisposingScope(DocumentClient);
        }
    }
}
