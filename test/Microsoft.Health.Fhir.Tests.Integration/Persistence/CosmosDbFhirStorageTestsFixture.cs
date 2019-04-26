// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Azure.Documents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.CosmosDb.Configs;
using Microsoft.Health.CosmosDb.Features.Storage;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Operations;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.StoredProcedures;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Versioning;
using NSubstitute;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    public class CosmosDbFhirStorageTestsFixture : IScoped<IFhirDataStore>, IScoped<IFhirOperationsDataStore>, IScoped<IFhirStorageTestHelper>
    {
        private readonly IDocumentClient _documentClient;
        private readonly CosmosDataStoreConfiguration _cosmosDataStoreConfiguration;
        private readonly CosmosCollectionConfiguration _cosmosCollectionConfiguration;

        private IFhirDataStore _fhirDataStore;
        private IFhirOperationsDataStore _fhirOperationsDataStore;
        private IFhirStorageTestHelper _fhirStorageTestHelper;

        private int _disposed = 0;

        public CosmosDbFhirStorageTestsFixture()
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

            var fhirRequestContextAccessor = new FhirRequestContextAccessor();

            var documentClientInitializer = new FhirDocumentClientInitializer(testProvider, fhirRequestContextAccessor, NullLogger<FhirDocumentClientInitializer>.Instance);
            _documentClient = documentClientInitializer.CreateDocumentClient(_cosmosDataStoreConfiguration);
            var fhirCollectionInitializer = new CollectionInitializer(_cosmosCollectionConfiguration.CollectionId, _cosmosDataStoreConfiguration, _cosmosCollectionConfiguration.InitialCollectionThroughput, upgradeManager, NullLogger<CollectionInitializer>.Instance);
            documentClientInitializer.InitializeDataStore(_documentClient, _cosmosDataStoreConfiguration, new List<ICollectionInitializer> { fhirCollectionInitializer }).GetAwaiter().GetResult();

            var cosmosDocumentQueryFactory = new FhirCosmosDocumentQueryFactory(Substitute.For<IFhirRequestContextAccessor>(), NullFhirDocumentQueryLogger.Instance);

            var fhirDataStoreContext = new FhirDataStoreContext(
                _cosmosDataStoreConfiguration,
                optionsMonitor);

            var documentClient = new NonDisposingScope(_documentClient);

            _fhirDataStore = new CosmosFhirDataStore(
                documentClient,
                fhirDataStoreContext,
                cosmosDocumentQueryFactory,
                new RetryExceptionPolicyFactory(_cosmosDataStoreConfiguration),
                NullLogger<CosmosFhirDataStore>.Instance);

            _fhirOperationsDataStore = new CosmosFhirOperationsDataStore(
                () => documentClient,
                fhirDataStoreContext,
                new RetryExceptionPolicyFactory(_cosmosDataStoreConfiguration),
                NullLogger<CosmosFhirOperationsDataStore>.Instance);

            _fhirStorageTestHelper = new CosmosDbFhirStorageTestHelper(_documentClient, fhirDataStoreContext);
        }

        IFhirDataStore IScoped<IFhirDataStore>.Value => _fhirDataStore;

        IFhirOperationsDataStore IScoped<IFhirOperationsDataStore>.Value => _fhirOperationsDataStore;

        IFhirStorageTestHelper IScoped<IFhirStorageTestHelper>.Value => _fhirStorageTestHelper;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _documentClient?.DeleteDocumentCollectionAsync(_cosmosDataStoreConfiguration.GetRelativeCollectionUri(_cosmosCollectionConfiguration.CollectionId)).GetAwaiter().GetResult();
                _documentClient?.Dispose();
            }
        }
    }
}
