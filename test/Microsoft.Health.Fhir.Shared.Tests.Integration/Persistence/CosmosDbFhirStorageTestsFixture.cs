// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions.Parsers;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.CosmosDb.Configs;
using Microsoft.Health.Fhir.CosmosDb.Features.Queries;
using Microsoft.Health.Fhir.CosmosDb.Features.Search;
using Microsoft.Health.Fhir.CosmosDb.Features.Search.Queries;
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
        private readonly IMediator _mediator = Substitute.For<IMediator>();
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

        private Container _container;
        private CosmosFhirDataStore _fhirDataStore;
        private IFhirOperationDataStore _fhirOperationDataStore;
        private IFhirStorageTestHelper _fhirStorageTestHelper;
        private FilebasedSearchParameterStatusDataStore _filebasedSearchParameterStatusDataStore;
        private ISearchParameterStatusDataStore _searchParameterStatusDataStore;
        private ISearchService _searchService;
        private SearchParameterDefinitionManager _searchParameterDefinitionManager;
        private SupportedSearchParameterDefinitionManager _supportedSearchParameterDefinitionManager;
        private SearchParameterStatusManager _searchParameterStatusManager;
        private CosmosClient _cosmosClient;

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
                InitialCollectionThroughput = 1000,
            };
        }

        public async Task InitializeAsync()
        {
            var fhirStoredProcs = typeof(IStoredProcedure).Assembly
                .GetTypes()
                .Where(x => !x.IsAbstract && typeof(IStoredProcedure).IsAssignableFrom(x))
                .ToArray()
                .Select(type => (IStoredProcedure)Activator.CreateInstance(type));

            var optionsMonitor = Substitute.For<IOptionsMonitor<CosmosCollectionConfiguration>>();

            optionsMonitor.Get(CosmosDb.Constants.CollectionConfigurationName).Returns(_cosmosCollectionConfiguration);

            _fhirRequestContextAccessor.RequestContext.CorrelationId.Returns(Guid.NewGuid().ToString());
            _fhirRequestContextAccessor.RequestContext.RouteName.Returns("routeName");

            _searchParameterDefinitionManager = new SearchParameterDefinitionManager(ModelInfoProvider.Instance, _mediator, () => _searchService.CreateMockScope(), NullLogger<SearchParameterDefinitionManager>.Instance);

            _supportedSearchParameterDefinitionManager = new SupportedSearchParameterDefinitionManager(_searchParameterDefinitionManager);
            var searchableSearchParameterDefinitionManager = new SearchableSearchParameterDefinitionManager(_searchParameterDefinitionManager, _fhirRequestContextAccessor);

            _filebasedSearchParameterStatusDataStore = new FilebasedSearchParameterStatusDataStore(_searchParameterDefinitionManager, ModelInfoProvider.Instance);

            IMediator mediator = Substitute.For<IMediator>();

            var updaters = new ICollectionUpdater[]
            {
                new FhirCollectionSettingsUpdater(_cosmosDataStoreConfiguration, optionsMonitor, NullLogger<FhirCollectionSettingsUpdater>.Instance),
                new StoredProcedureInstaller(fhirStoredProcs),
                new CosmosDbSearchParameterStatusInitializer(
                    () => _filebasedSearchParameterStatusDataStore,
                    new CosmosQueryFactory(
                        new CosmosResponseProcessor(_fhirRequestContextAccessor, mediator, Substitute.For<ICosmosQueryLogger>(), NullLogger<CosmosResponseProcessor>.Instance),
                        NullFhirCosmosQueryLogger.Instance),
                    _cosmosDataStoreConfiguration),
            };

            var dbLock = new CosmosDbDistributedLockFactory(Substitute.For<Func<IScoped<Container>>>(), NullLogger<CosmosDbDistributedLock>.Instance);

            var upgradeManager = new CollectionUpgradeManager(updaters, _cosmosDataStoreConfiguration, optionsMonitor, dbLock, NullLogger<CollectionUpgradeManager>.Instance);
            ICosmosClientTestProvider testProvider = new CosmosClientReadWriteTestProvider();

            var cosmosResponseProcessor = Substitute.For<ICosmosResponseProcessor>();

            var responseProcessor = new CosmosResponseProcessor(_fhirRequestContextAccessor, mediator, Substitute.For<ICosmosQueryLogger>(), NullLogger<CosmosResponseProcessor>.Instance);
            var handler = new FhirCosmosResponseHandler(() => new NonDisposingScope(_container), _cosmosDataStoreConfiguration, _fhirRequestContextAccessor, responseProcessor);
            var retryExceptionPolicyFactory = new RetryExceptionPolicyFactory(_cosmosDataStoreConfiguration, _fhirRequestContextAccessor);
            var documentClientInitializer = new FhirCosmosClientInitializer(testProvider, () => new[] { handler }, retryExceptionPolicyFactory, NullLogger<FhirCosmosClientInitializer>.Instance);
            _cosmosClient = documentClientInitializer.CreateCosmosClient(_cosmosDataStoreConfiguration);
            var fhirCollectionInitializer = new CollectionInitializer(_cosmosCollectionConfiguration, _cosmosDataStoreConfiguration, upgradeManager, retryExceptionPolicyFactory, testProvider, NullLogger<CollectionInitializer>.Instance);

            // Cosmos DB emulators throws errors when multiple collections are initialized concurrently.
            // Use the semaphore to only allow one initialization at a time.
            await CollectionInitializationSemaphore.WaitAsync();

            try
            {
                await documentClientInitializer.InitializeDataStore(_cosmosClient, _cosmosDataStoreConfiguration, new List<ICollectionInitializer> { fhirCollectionInitializer });
                _container = documentClientInitializer.CreateFhirContainer(_cosmosClient, _cosmosDataStoreConfiguration.DatabaseId, _cosmosCollectionConfiguration.CollectionId);
            }
            finally
            {
                CollectionInitializationSemaphore.Release();
            }

            var cosmosDocumentQueryFactory = new CosmosQueryFactory(cosmosResponseProcessor, NullFhirCosmosQueryLogger.Instance);

            var documentClient = new NonDisposingScope(_container);

            _searchParameterStatusDataStore = new CosmosDbSearchParameterStatusDataStore(
                () => documentClient,
                _cosmosDataStoreConfiguration,
                cosmosDocumentQueryFactory);

            IOptions<CoreFeatureConfiguration> options = Options.Create(new CoreFeatureConfiguration());

            _fhirDataStore = new CosmosFhirDataStore(
                documentClient,
                _cosmosDataStoreConfiguration,
                optionsMonitor,
                cosmosDocumentQueryFactory,
                retryExceptionPolicyFactory,
                NullLogger<CosmosFhirDataStore>.Instance,
                options,
                new Lazy<ISupportedSearchParameterDefinitionManager>(_supportedSearchParameterDefinitionManager),
                ModelInfoProvider.Instance);

            _fhirOperationDataStore = new CosmosFhirOperationDataStore(
                documentClient,
                _cosmosDataStoreConfiguration,
                optionsMonitor,
                retryExceptionPolicyFactory,
                new CosmosQueryFactory(responseProcessor, new NullFhirCosmosQueryLogger()),
                NullLogger<CosmosFhirOperationDataStore>.Instance);

            var searchParameterExpressionParser = new SearchParameterExpressionParser(new ReferenceSearchValueParser(_fhirRequestContextAccessor));
            var expressionParser = new ExpressionParser(() => searchableSearchParameterDefinitionManager, searchParameterExpressionParser, _fhirRequestContextAccessor);
            ISortingValidator sortingValidator = Substitute.For<ISortingValidator>();
            sortingValidator.ValidateSorting(Arg.Is<IReadOnlyList<(SearchParameterInfo searchParameter, SortOrder sortOrder)>>(x => x[0].searchParameter.Name == KnownQueryParameterNames.LastUpdated), out Arg.Any<IReadOnlyList<string>>()).Returns(true);
            var searchOptionsFactory = new SearchOptionsFactory(expressionParser, () => searchableSearchParameterDefinitionManager, options, _fhirRequestContextAccessor, sortingValidator, NullLogger<SearchOptionsFactory>.Instance);

            var compartmentDefinitionManager = new CompartmentDefinitionManager(ModelInfoProvider.Instance);
            await compartmentDefinitionManager.StartAsync(CancellationToken.None);
            var compartmentSearchRewriter = new CompartmentSearchRewriter(new Lazy<ICompartmentDefinitionManager>(() => compartmentDefinitionManager), new Lazy<ISearchParameterDefinitionManager>(() => _searchParameterDefinitionManager));
            var smartCompartmentSearchRewriter = new SmartCompartmentSearchRewriter(compartmentSearchRewriter, new Lazy<ISearchParameterDefinitionManager>(() => _searchParameterDefinitionManager));

            ICosmosDbCollectionPhysicalPartitionInfo cosmosDbPhysicalPartitionInfo = Substitute.For<ICosmosDbCollectionPhysicalPartitionInfo>();
            cosmosDbPhysicalPartitionInfo.PhysicalPartitionCount.Returns(1);

            _searchService = new FhirCosmosSearchService(
                searchOptionsFactory,
                _fhirDataStore,
                new QueryBuilder(),
                _fhirRequestContextAccessor,
                _cosmosDataStoreConfiguration,
                cosmosDbPhysicalPartitionInfo,
                new QueryPartitionStatisticsCache(),
                compartmentSearchRewriter,
                smartCompartmentSearchRewriter,
                NullLogger<FhirCosmosSearchService>.Instance);

            await _searchParameterDefinitionManager.EnsureInitializedAsync(CancellationToken.None);

            ISearchParameterSupportResolver searchParameterSupportResolver = Substitute.For<ISearchParameterSupportResolver>();
            searchParameterSupportResolver.IsSearchParameterSupported(Arg.Any<SearchParameterInfo>()).Returns((true, false));

            _searchParameterStatusManager = new SearchParameterStatusManager(
                _searchParameterStatusDataStore,
                _searchParameterDefinitionManager,
                searchParameterSupportResolver,
                mediator,
                NullLogger<SearchParameterStatusManager>.Instance);

            _fhirStorageTestHelper = new CosmosDbFhirStorageTestHelper(_container);
        }

        public async Task DisposeAsync()
        {
            if (_container != null)
            {
                await _container.DeleteContainerAsync();
            }

            _cosmosClient.Dispose();
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

            if (serviceType == typeof(ISearchParameterStatusDataStore))
            {
                return _searchParameterStatusDataStore;
            }

            if (serviceType == typeof(FilebasedSearchParameterStatusDataStore))
            {
                return _filebasedSearchParameterStatusDataStore;
            }

            if (serviceType == typeof(ISearchService))
            {
                return _searchService;
            }

            if (serviceType == typeof(SearchParameterDefinitionManager))
            {
                return _searchParameterDefinitionManager;
            }

            if (serviceType == typeof(SupportedSearchParameterDefinitionManager))
            {
                return _supportedSearchParameterDefinitionManager;
            }

            if (serviceType == typeof(SearchParameterStatusManager))
            {
                return _searchParameterStatusManager;
            }

            if (serviceType == typeof(RequestContextAccessor<IFhirRequestContext>))
            {
                return _fhirRequestContextAccessor;
            }

            return null;
        }
    }
}
