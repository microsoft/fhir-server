// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Documents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.CosmosDb.Configs;
using Microsoft.Health.CosmosDb.Features.Storage;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Export;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.StoredProcedures;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Versioning;
using NSubstitute;
using NonDisposingScope = Microsoft.Health.CosmosDb.Features.Storage.NonDisposingScope;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    public class IntegrationTestCosmosDataStore : IDataStore, IDisposable
    {
        private readonly IDocumentClient _documentClient;
        private readonly FhirDataStore _dataStore;
        private readonly CosmosDataStoreConfiguration _cosmosDataStoreConfiguration;
        private readonly CosmosCollectionConfiguration _cosmosCollectionConfiguration;

        public IntegrationTestCosmosDataStore()
        {
            _cosmosDataStoreConfiguration = new CosmosDataStoreConfiguration
            {
                Host = Environment.GetEnvironmentVariable("CosmosDb:Host") ?? CosmosDbLocalEmulator.Host,
                Key = Environment.GetEnvironmentVariable("CosmosDb:Key") ?? CosmosDbLocalEmulator.Key,
                DatabaseId = Environment.GetEnvironmentVariable("CosmosDb:DatabaseId") ?? "FhirTests",
                AllowDatabaseCreation = true,
                PreferredLocations = Environment.GetEnvironmentVariable("CosmosDb:PreferredLocations")?.Split(';', StringSplitOptions.RemoveEmptyEntries),
            };

            _cosmosCollectionConfiguration = new CosmosCollectionConfiguration
            {
                CollectionId = Guid.NewGuid().ToString(),
            };

            var fhirStoredProcs = typeof(IFhirStoredProcedure).Assembly
                .GetTypes()
                .Where(x => !x.IsAbstract && typeof(IFhirStoredProcedure).IsAssignableFrom(x))
                .ToArray()
                .Select(type => (IFhirStoredProcedure)Activator.CreateInstance(type));

            var optionsMonitor = Substitute.For<IOptionsMonitor<CosmosCollectionConfiguration>>();

            optionsMonitor.Get(CosmosDb.Constants.CollectionConfigurationName).Returns(_cosmosCollectionConfiguration);

            var updaters = new IFhirCollectionUpdater[]
            {
                new FhirCollectionSettingsUpdater(_cosmosDataStoreConfiguration, optionsMonitor, NullLogger<FhirCollectionSettingsUpdater>.Instance),
                new FhirStoredProcedureInstaller(fhirStoredProcs),
            };

            var dbLock = new CosmosDbDistributedLockFactory(Substitute.For<Func<IScoped<IDocumentClient>>>(), NullLogger<CosmosDbDistributedLock>.Instance);

            var upgradeManager = new FhirCollectionUpgradeManager(updaters, _cosmosDataStoreConfiguration, optionsMonitor, dbLock, NullLogger<FhirCollectionUpgradeManager>.Instance);
            IDocumentClientTestProvider testProvider = new DocumentClientReadWriteTestProvider();

            var documentClientInitializer = new DocumentClientInitializer(testProvider, NullLogger<DocumentClientInitializer>.Instance);
            _documentClient = documentClientInitializer.CreateDocumentClient(_cosmosDataStoreConfiguration);
            var fhirCollectionInitializer = new CollectionInitializer(_cosmosCollectionConfiguration.CollectionId, _cosmosDataStoreConfiguration, _cosmosCollectionConfiguration.InitialCollectionThroughput, upgradeManager, NullLogger<CollectionInitializer>.Instance);
            documentClientInitializer.InitializeDataStore(_documentClient, _cosmosDataStoreConfiguration, new List<ICollectionInitializer> { fhirCollectionInitializer }).GetAwaiter().GetResult();

            var cosmosDocumentQueryFactory = new FhirCosmosDocumentQueryFactory(Substitute.For<IFhirRequestContextAccessor>(), NullFhirDocumentQueryLogger.Instance);
            var fhirRequestContextAccessor = new FhirRequestContextAccessor();

            _dataStore = new FhirDataStore(
                new NonDisposingScope(_documentClient),
                _cosmosDataStoreConfiguration,
                cosmosDocumentQueryFactory,
                new RetryExceptionPolicyFactory(_cosmosDataStoreConfiguration),
                fhirRequestContextAccessor,
                optionsMonitor,
                NullLogger<FhirDataStore>.Instance);
        }

        public void Dispose()
        {
            _documentClient?.DeleteDocumentCollectionAsync(_cosmosDataStoreConfiguration.GetRelativeCollectionUri(_cosmosCollectionConfiguration.CollectionId)).GetAwaiter().GetResult();
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

        public async Task<HttpStatusCode> UpsertExportJobAsync(ExportJobRecord jobRecord, CancellationToken cancellationToken = default)
        {
            return await _dataStore.UpsertExportJobAsync(jobRecord, cancellationToken);
        }

        public async Task<ExportJobRecord> GetExportJobAsync(string jobId, CancellationToken cancellationToken = default)
        {
            return await _dataStore.GetExportJobAsync(jobId, cancellationToken);
        }
    }
}
