// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Documents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.CosmosDb.Configs;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Continuation;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.StoredProcedures;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Versioning;
using NSubstitute;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    public class IntegrationTestCosmosDataStore : IDataStore, IContinuationTokenCache, IDisposable
    {
        private readonly IDocumentClient _documentClient;
        private readonly CosmosDataStore _dataStore;
        private readonly CosmosDataStoreConfiguration _cosmosDataStoreConfiguration;

        public IntegrationTestCosmosDataStore()
        {
            _cosmosDataStoreConfiguration = new CosmosDataStoreConfiguration
            {
                Host = Environment.GetEnvironmentVariable("CosmosDb:Host") ?? CosmosDbLocalEmulator.Host,
                Key = Environment.GetEnvironmentVariable("CosmosDb:Key") ?? CosmosDbLocalEmulator.Key,
                DatabaseId = Environment.GetEnvironmentVariable("CosmosDb:DatabaseId") ?? "FhirTests",
                CollectionId = Guid.NewGuid().ToString(),
                AllowDatabaseCreation = true,
                PreferredLocations = Environment.GetEnvironmentVariable("CosmosDb:PreferredLocations")?.Split(';', StringSplitOptions.RemoveEmptyEntries),
            };

            var updaters = new ICollectionUpdater[]
            {
                new CollectionSettingsUpdater(NullLogger<CollectionSettingsUpdater>.Instance, _cosmosDataStoreConfiguration),
                new StoredProcedureInstaller(),
            };

            var dbLock = new CosmosDbDistributedLockFactory(Substitute.For<Func<IScoped<IDocumentClient>>>(), NullLogger<CosmosDbDistributedLock>.Instance);

            var upgradeManager = new CollectionUpgradeManager(updaters, _cosmosDataStoreConfiguration, dbLock, NullLogger<CollectionUpgradeManager>.Instance);
            IDocumentClientTestProvider testProvider = new DocumentClientReadWriteTestProvider();

            var documentClientInitializer = new DocumentClientInitializer(testProvider, NullLogger<DocumentClientInitializer>.Instance, upgradeManager, Substitute.For<IFhirRequestContextAccessor>());
            _documentClient = documentClientInitializer.CreateDocumentClient(_cosmosDataStoreConfiguration);
            documentClientInitializer.InitializeDataStore(_documentClient, _cosmosDataStoreConfiguration).GetAwaiter().GetResult();

            var cosmosDocumentQueryFactory = new CosmosDocumentQueryFactory(NullCosmosDocumentQueryLogger.Instance);
            _dataStore = new CosmosDataStore(
                new NonDisposingScope(_documentClient),
                _cosmosDataStoreConfiguration,
                cosmosDocumentQueryFactory,
                new RetryExceptionPolicyFactory(_cosmosDataStoreConfiguration),
                NullLogger<CosmosDataStore>.Instance);
        }

        public void Dispose()
        {
            _documentClient?.DeleteDocumentCollectionAsync(_cosmosDataStoreConfiguration.RelativeCollectionUri).GetAwaiter().GetResult();
            _documentClient?.Dispose();
        }

        public async Task<UpsertOutcome> UpsertAsync(
            ResourceWrapper resource,
            WeakETag weakETag,
            bool allowCreate,
            bool keepHistory,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return await _dataStore.UpsertAsync(resource, weakETag, allowCreate, keepHistory, cancellationToken);
        }

        public async Task<ResourceWrapper> GetAsync(ResourceKey key, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await _dataStore.GetAsync(key, cancellationToken);
        }

        public async Task HardDeleteAsync(ResourceKey key, CancellationToken cancellationToken = default(CancellationToken))
        {
            await _dataStore.HardDeleteAsync(key, cancellationToken);
        }

        public Task<string> GetContinuationTokenAsync(string id, CancellationToken cancellationToken = default)
        {
            return _dataStore.GetContinuationTokenAsync(id, cancellationToken);
        }

        public Task<string> SaveContinuationTokenAsync(string continuationToken, CancellationToken cancellationToken = default)
        {
            return _dataStore.SaveContinuationTokenAsync(continuationToken, cancellationToken);
        }
    }
}
