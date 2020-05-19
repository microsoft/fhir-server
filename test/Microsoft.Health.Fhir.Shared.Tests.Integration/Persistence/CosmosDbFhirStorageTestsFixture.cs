// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Serialization;
using MediatR;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.CosmosDb.Configs;
using Microsoft.Health.CosmosDb.Features.Storage;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Operations;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Registry;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.StoredProcedures;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Versioning;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    public class CosmosDbFhirStorageTestsFixture : IServiceProvider, IAsyncLifetime
    {
        private static readonly SemaphoreSlim CollectionInitializationSemaphore = new SemaphoreSlim(1, 1);

        private readonly CosmosDataStoreConfiguration _cosmosDataStoreConfiguration;
        private readonly CosmosCollectionConfiguration _cosmosCollectionConfiguration;

        private IDocumentClient _documentClient;
        private IFhirDataStore _fhirDataStore;
        private IFhirOperationDataStore _fhirOperationDataStore;
        private IFhirStorageTestHelper _fhirStorageTestHelper;
        private FilebasedSearchParameterRegistryDataStore _filebasedSearchParameterRegistry;

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
        }

        public async Task InitializeAsync()
        {
            var fhirStoredProcs = typeof(IFhirStoredProcedure).Assembly
                .GetTypes()
                .Where(x => !x.IsAbstract && typeof(IFhirStoredProcedure).IsAssignableFrom(x))
                .ToArray()
                .Select(type => (IFhirStoredProcedure)Activator.CreateInstance(type));

            var optionsMonitor = Substitute.For<IOptionsMonitor<CosmosCollectionConfiguration>>();

            optionsMonitor.Get(CosmosDb.Constants.CollectionConfigurationName).Returns(_cosmosCollectionConfiguration);

            Type searchDefinitionManagerType = typeof(SearchParameterDefinitionManager);
            var searchParameterDefinitionManager = new SearchParameterDefinitionManager(new FhirJsonParser(), ModelInfoProvider.Instance);
            searchParameterDefinitionManager.Start();
            _filebasedSearchParameterRegistry = new FilebasedSearchParameterRegistryDataStore(
                searchParameterDefinitionManager,
                searchDefinitionManagerType.Assembly,
                $"{searchDefinitionManagerType.Namespace}.unsupported-search-parameters.json");

            var updaters = new IFhirCollectionUpdater[]
            {
                new FhirCollectionSettingsUpdater(_cosmosDataStoreConfiguration, optionsMonitor, NullLogger<FhirCollectionSettingsUpdater>.Instance),
                new FhirStoredProcedureInstaller(fhirStoredProcs),
                new CosmosDbStatusRegistryInitializer(
                    () => _filebasedSearchParameterRegistry,
                    new FhirCosmosDocumentQueryFactory(
                        new CosmosResponseProcessor(Substitute.For<IFhirRequestContextAccessor>(), Substitute.For<IMediator>(), NullLogger<CosmosResponseProcessor>.Instance),
                        NullFhirDocumentQueryLogger.Instance)),
            };

            var dbLock = new CosmosDbDistributedLockFactory(Substitute.For<Func<IScoped<IDocumentClient>>>(), NullLogger<CosmosDbDistributedLock>.Instance);

            var upgradeManager = new FhirCollectionUpgradeManager(updaters, _cosmosDataStoreConfiguration, optionsMonitor, dbLock, NullLogger<FhirCollectionUpgradeManager>.Instance);
            IDocumentClientTestProvider testProvider = new DocumentClientReadWriteTestProvider();

            var fhirRequestContextAccessor = new FhirRequestContextAccessor();
            var cosmosResponseProcessor = Substitute.For<ICosmosResponseProcessor>();

            var documentClientInitializer = new FhirDocumentClientInitializer(testProvider, fhirRequestContextAccessor, cosmosResponseProcessor, NullLogger<FhirDocumentClientInitializer>.Instance);
            _documentClient = documentClientInitializer.CreateDocumentClient(_cosmosDataStoreConfiguration);
            var fhirCollectionInitializer = new CollectionInitializer(_cosmosCollectionConfiguration.CollectionId, _cosmosDataStoreConfiguration, _cosmosCollectionConfiguration.InitialCollectionThroughput, upgradeManager, NullLogger<CollectionInitializer>.Instance);

            // Cosmos DB emulators throws errors when multiple collections are initialized concurrently.
            // Use the semaphore to only allow one initialization at a time.
            await CollectionInitializationSemaphore.WaitAsync();

            try
            {
                await documentClientInitializer.InitializeDataStore(_documentClient, _cosmosDataStoreConfiguration, new List<ICollectionInitializer> { fhirCollectionInitializer });
            }
            finally
            {
                CollectionInitializationSemaphore.Release();
            }

            var cosmosDocumentQueryFactory = new FhirCosmosDocumentQueryFactory(cosmosResponseProcessor, NullFhirDocumentQueryLogger.Instance);

            var documentClient = new NonDisposingScope(_documentClient);

            _fhirDataStore = new CosmosFhirDataStore(
                documentClient,
                _cosmosDataStoreConfiguration,
                optionsMonitor,
                cosmosDocumentQueryFactory,
                new RetryExceptionPolicyFactory(_cosmosDataStoreConfiguration),
                NullLogger<CosmosFhirDataStore>.Instance,
                new VersionSpecificModelInfoProvider(),
                Options.Create(new CoreFeatureConfiguration()));

            _fhirOperationDataStore = new CosmosFhirOperationDataStore(
                documentClient,
                _cosmosDataStoreConfiguration,
                optionsMonitor,
                new RetryExceptionPolicyFactory(_cosmosDataStoreConfiguration),
                NullLogger<CosmosFhirOperationDataStore>.Instance);

            _fhirStorageTestHelper = new CosmosDbFhirStorageTestHelper(
                _documentClient,
                _cosmosDataStoreConfiguration.DatabaseId,
                _cosmosCollectionConfiguration.CollectionId);
        }

        public async Task DisposeAsync()
        {
            using (_documentClient as IDisposable)
            {
                await _documentClient?.DeleteDocumentCollectionAsync(_cosmosDataStoreConfiguration.GetRelativeCollectionUri(_cosmosCollectionConfiguration.CollectionId));
            }
        }

        object IServiceProvider.GetService(Type serviceType)
        {
            if (serviceType == typeof(IFhirDataStore))
            {
                return _fhirDataStore;
            }

            if (serviceType == typeof(IFhirOperationDataStore))
            {
                return _fhirOperationDataStore;
            }

            if (serviceType == typeof(IFhirStorageTestHelper))
            {
                return _fhirStorageTestHelper;
            }

            if (serviceType.IsInstanceOfType(this))
            {
                return this;
            }

            return null;
        }
    }
}
