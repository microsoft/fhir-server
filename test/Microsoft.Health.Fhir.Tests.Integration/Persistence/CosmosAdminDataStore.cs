// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Documents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.CosmosDb.Configs;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Continuation;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.StoredProcedures;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Versioning;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    public class CosmosAdminDataStore : IDataStore, IContinuationTokenCache, IDisposable, ISecurityDataStore
    {
        private readonly IDocumentClient _documentClient;
        private readonly CosmosDataStore _dataStore;
        private readonly CosmosDataStoreConfiguration _cosmosDataStoreConfiguration;

        public CosmosAdminDataStore()
        {
            _cosmosDataStoreConfiguration = new CosmosDataStoreConfiguration
            {
                Host = Environment.GetEnvironmentVariable("DataStore:Host") ?? CosmosDbLocalEmulator.Host,
                Key = Environment.GetEnvironmentVariable("DataStore:Key") ?? CosmosDbLocalEmulator.Key,
                DatabaseId = Environment.GetEnvironmentVariable("DataStore:DatabaseId") ?? "FhirTests",
                CollectionId = Guid.NewGuid().ToString(),
                AllowDatabaseCreation = true,
                PreferredLocations = Environment.GetEnvironmentVariable("DataStore:PreferredLocations")?.Split(';', StringSplitOptions.RemoveEmptyEntries),
            };

            var updaters = new ICollectionUpdater[]
            {
                new CollectionSettingsUpdater(NullLogger<CollectionSettingsUpdater>.Instance, _cosmosDataStoreConfiguration),
                new StoredProcedureInstaller(),
            };

            var dbLock = new CosmosDbDistributedLockFactory(NullLogger<CosmosDbDistributedLock>.Instance);

            var upgradeManager = new CollectionUpgradeManager(updaters, _cosmosDataStoreConfiguration, dbLock, NullLogger<CollectionUpgradeManager>.Instance);
            IDocumentClientTestProvider testProvider = new DocumentClientReadWriteTestProvider();

            var documentClientInitializer = new DocumentClientInitializer(testProvider, NullLogger<DocumentClientInitializer>.Instance, upgradeManager);
            _documentClient = documentClientInitializer.CreateDocumentClient(_cosmosDataStoreConfiguration);
            documentClientInitializer.InitializeDataStore(_documentClient, _cosmosDataStoreConfiguration).GetAwaiter().GetResult();

            var cosmosDocumentQueryFactory = new CosmosDocumentQueryFactory(() => _documentClient, NullCosmosDocumentQueryLogger.Instance);
            _dataStore = new CosmosDataStore(() => _documentClient, _cosmosDataStoreConfiguration, cosmosDocumentQueryFactory, NullLogger<CosmosDataStore>.Instance);
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

        public Task<IEnumerable<Role>> GetAllRolesAsync(CancellationToken cancellationToken)
        {
            return _dataStore.GetAllRolesAsync(cancellationToken);
        }

        public Task<Role> GetRoleAsync(string name, CancellationToken cancellationToken)
        {
            return _dataStore.GetRoleAsync(name, cancellationToken);
        }

        public Task<Role> UpsertRoleAsync(Role role, WeakETag weakETag, CancellationToken cancellationToken)
        {
            return _dataStore.UpsertRoleAsync(role, weakETag, cancellationToken);
        }

        public Task DeleteRoleAsync(string name, CancellationToken cancellationToken)
        {
            return _dataStore.DeleteRoleAsync(name, cancellationToken);
        }
    }
}
