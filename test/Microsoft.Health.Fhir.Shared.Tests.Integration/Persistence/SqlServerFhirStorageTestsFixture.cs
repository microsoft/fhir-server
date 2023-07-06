// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Abstractions.Features.Transactions;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Access;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions.Parsers;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.SqlServer.Features.Storage.Registry;
using Microsoft.Health.JobManagement;
using Microsoft.Health.JobManagement.UnitTests;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Configs;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Schema.Manager;
using Microsoft.Health.SqlServer.Features.Storage;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    public class SqlServerFhirStorageTestsFixture : IServiceProvider, IAsyncLifetime
    {
        private const string LocalConnectionString = "server=(local);Integrated Security=true;TrustServerCertificate=True";
        private const string MasterDatabaseName = "master";

        private readonly int _maximumSupportedSchemaVersion;
        private readonly string _databaseName;
        private readonly SqlServerFhirDataStore _fhirDataStore;
        private readonly IFhirOperationDataStore _fhirOperationDataStore;
        private readonly SqlServerFhirOperationDataStore _sqlServerFhirOperationDataStore;
        private readonly SqlServerFhirStorageTestHelper _testHelper;
        private readonly SchemaInitializer _schemaInitializer;
        private readonly SchemaUpgradeRunner _schemaUpgradeRunner;
        private readonly FilebasedSearchParameterStatusDataStore _filebasedSearchParameterStatusDataStore;
        private readonly ISearchService _searchService;
        private readonly SearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly SupportedSearchParameterDefinitionManager _supportedSearchParameterDefinitionManager;
        private readonly SearchParameterStatusManager _searchParameterStatusManager;
        private readonly IMediator _mediator = Substitute.For<IMediator>();
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
        private readonly SqlQueueClient _sqlQueueClient;

        public SqlServerFhirStorageTestsFixture()
            : this(SchemaVersionConstants.Max, $"FHIRINTEGRATIONTEST_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}_{BigInteger.Abs(new BigInteger(Guid.NewGuid().ToByteArray()))}")
        {
        }

        internal SqlServerFhirStorageTestsFixture(int maximumSupportedSchemaVersion, string databaseName, IOptions<CoreFeatureConfiguration> coreFeatures = null)
        {
            var initialConnectionString = Environment.GetEnvironmentVariable("SqlServer:ConnectionString") ?? LocalConnectionString;

            _maximumSupportedSchemaVersion = maximumSupportedSchemaVersion;
            _databaseName = databaseName;
            TestConnectionString = new SqlConnectionStringBuilder(initialConnectionString) { InitialCatalog = _databaseName }.ToString();

            var schemaOptions = new SqlServerSchemaOptions { AutomaticUpdatesEnabled = true };
            SqlServerDataStoreConfiguration = Options.Create(new SqlServerDataStoreConfiguration
            {
                ConnectionString = TestConnectionString,
                Initialize = true,
                SchemaOptions = schemaOptions,
                StatementTimeout = TimeSpan.FromMinutes(10),
                CommandTimeout = TimeSpan.FromMinutes(3),
            });

            SchemaInformation = new SchemaInformation(SchemaVersionConstants.Min, maximumSupportedSchemaVersion);
            var scriptProvider = new ScriptProvider<SchemaVersion>();
            var baseScriptProvider = new BaseScriptProvider();
            var mediator = Substitute.For<IMediator>();
            var sqlSortingValidator = new SqlServerSortingValidator(SchemaInformation);
            SqlRetryLogicBaseProvider sqlRetryLogicBaseProvider = SqlConfigurableRetryFactory.CreateFixedRetryProvider(new SqlClientRetryOptions().Settings);

            var sqlConnectionStringProvider = new DefaultSqlConnectionStringProvider(SqlServerDataStoreConfiguration);
            SqlConnectionBuilder = new DefaultSqlConnectionBuilder(sqlConnectionStringProvider, sqlRetryLogicBaseProvider);

            var sqlConnection = Substitute.For<ISqlConnectionBuilder>();
            sqlConnection.GetSqlConnectionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ReturnsForAnyArgs((x) => Task.FromResult(GetSqlConnection(TestConnectionString)));
            var sqlConnectionWrapperFactory = new SqlConnectionWrapperFactory(new SqlTransactionHandler(), sqlConnection, sqlRetryLogicBaseProvider, SqlServerDataStoreConfiguration);
            var schemaManagerDataStore = new SchemaManagerDataStore(sqlConnectionWrapperFactory, SqlServerDataStoreConfiguration, NullLogger<SchemaManagerDataStore>.Instance);
            _schemaUpgradeRunner = new SchemaUpgradeRunner(scriptProvider, baseScriptProvider, NullLogger<SchemaUpgradeRunner>.Instance, sqlConnectionWrapperFactory, schemaManagerDataStore);

            Func<IServiceProvider, SchemaUpgradeRunner> schemaUpgradeRunnerFactory = p => _schemaUpgradeRunner;
            Func<IServiceProvider, IReadOnlySchemaManagerDataStore> schemaManagerDataStoreFactory = p => schemaManagerDataStore;
            Func<IServiceProvider, ISqlConnectionStringProvider> sqlConnectionStringProviderFunc = p => sqlConnectionStringProvider;
            Func<IServiceProvider, SqlConnectionWrapperFactory> sqlConnectionWrapperFactoryFunc = p => sqlConnectionWrapperFactory;

            var collection = new ServiceCollection();
            collection.AddScoped(sqlConnectionStringProviderFunc);
            collection.AddScoped(sqlConnectionWrapperFactoryFunc);
            collection.AddScoped(schemaUpgradeRunnerFactory);
            collection.AddScoped(schemaManagerDataStoreFactory);
            var serviceProviderSchemaInitializer = collection.BuildServiceProvider();
            _schemaInitializer = new SchemaInitializer(serviceProviderSchemaInitializer, SqlServerDataStoreConfiguration, SchemaInformation, mediator, NullLogger<SchemaInitializer>.Instance);

            _searchParameterDefinitionManager = new SearchParameterDefinitionManager(ModelInfoProvider.Instance, _mediator, () => _searchService.CreateMockScope(), NullLogger<SearchParameterDefinitionManager>.Instance);

            _filebasedSearchParameterStatusDataStore = new FilebasedSearchParameterStatusDataStore(_searchParameterDefinitionManager, ModelInfoProvider.Instance);

            var securityConfiguration = new SecurityConfiguration { PrincipalClaims = { "oid" } };

            SqlTransactionHandler = new SqlTransactionHandler();
            SqlConnectionWrapperFactory = new SqlConnectionWrapperFactory(SqlTransactionHandler, SqlConnectionBuilder, sqlRetryLogicBaseProvider, SqlServerDataStoreConfiguration);

            var sqlServerFhirModel = new SqlServerFhirModel(
                SchemaInformation,
                _searchParameterDefinitionManager,
                () => _filebasedSearchParameterStatusDataStore,
                Options.Create(securityConfiguration),
                () => SqlConnectionWrapperFactory.CreateMockScope(),
                Substitute.For<IMediator>(),
                NullLogger<SqlServerFhirModel>.Instance);
            SqlServerFhirModel = sqlServerFhirModel;

            var searchParameterToSearchValueTypeMap = new SearchParameterToSearchValueTypeMap();

            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSqlServerTableRowParameterGenerators();
            serviceCollection.AddSingleton(sqlServerFhirModel);
            serviceCollection.AddSingleton<ISqlServerFhirModel>(sqlServerFhirModel);
            serviceCollection.AddSingleton(searchParameterToSearchValueTypeMap);
            var converter = (ICompressedRawResourceConverter)new CompressedRawResourceConverter();
            serviceCollection.AddSingleton(converter);

            ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();

            var upsertResourceTvpGeneratorVLatest = serviceProvider.GetRequiredService<VLatest.UpsertResourceTvpGenerator<IReadOnlyList<ResourceWrapper>>>();
            var reindexResourceTvpGeneratorVLatest = serviceProvider.GetRequiredService<VLatest.ReindexResourceTvpGenerator<IReadOnlyList<ResourceWrapper>>>();
            var bulkReindexResourceTvpGeneratorVLatest = serviceProvider.GetRequiredService<VLatest.BulkReindexResourcesTvpGenerator<IReadOnlyList<ResourceWrapper>>>();
            var upsertSearchParamsTvpGenerator = serviceProvider.GetRequiredService<VLatest.UpsertSearchParamsTvpGenerator<List<ResourceSearchParameterStatus>>>();

            _supportedSearchParameterDefinitionManager = new SupportedSearchParameterDefinitionManager(_searchParameterDefinitionManager);

            SqlServerSearchParameterStatusDataStore = new SqlServerSearchParameterStatusDataStore(
                () => SqlConnectionWrapperFactory.CreateMockScope(),
                upsertSearchParamsTvpGenerator,
                () => _filebasedSearchParameterStatusDataStore,
                SchemaInformation,
                sqlSortingValidator,
                sqlServerFhirModel,
                _searchParameterDefinitionManager);

            IOptions<CoreFeatureConfiguration> options = coreFeatures ?? Options.Create(new CoreFeatureConfiguration());

            var bundleConfiguration = new BundleConfiguration() { SupportsBundleOrchestrator = true };
            var bundleOptions = Substitute.For<IOptions<BundleConfiguration>>();
            bundleOptions.Value.Returns(bundleConfiguration);

            var bundleOrchestrator = new BundleOrchestrator(bundleOptions, NullLogger<BundleOrchestrator>.Instance);

            SqlRetryService = new SqlRetryService(SqlConnectionBuilder, SqlServerDataStoreConfiguration, Options.Create(new SqlRetryServiceOptions()), new SqlRetryServiceDelegateOptions());

            _fhirDataStore = new SqlServerFhirDataStore(
                sqlServerFhirModel,
                searchParameterToSearchValueTypeMap,
                upsertResourceTvpGeneratorVLatest,
                reindexResourceTvpGeneratorVLatest,
                bulkReindexResourceTvpGeneratorVLatest,
                options,
                bundleOrchestrator,
                SqlRetryService,
                SqlConnectionWrapperFactory,
                converter,
                NullLogger<SqlServerFhirDataStore>.Instance,
                SchemaInformation,
                ModelInfoProvider.Instance,
                _fhirRequestContextAccessor);

            // the test queue client may not be enough for these tests. will need to look back into this.
            var queueClient = new TestQueueClient();
            _fhirOperationDataStore = new SqlServerFhirOperationDataStore(queueClient, NullLoggerFactory.Instance);

            var sqlQueueClient = new SqlQueueClient(SqlConnectionWrapperFactory, SchemaInformation, SqlRetryService, NullLogger<SqlQueueClient>.Instance);
            _sqlServerFhirOperationDataStore = new SqlServerFhirOperationDataStore(sqlQueueClient, NullLoggerFactory.Instance);

            _fhirRequestContextAccessor.RequestContext.CorrelationId.Returns(Guid.NewGuid().ToString());
            _fhirRequestContextAccessor.RequestContext.RouteName.Returns("routeName");

            var searchableSearchParameterDefinitionManager = new SearchableSearchParameterDefinitionManager(_searchParameterDefinitionManager, _fhirRequestContextAccessor);
            var searchParameterExpressionParser = new SearchParameterExpressionParser(new ReferenceSearchValueParser(_fhirRequestContextAccessor));
            var expressionParser = new ExpressionParser(() => searchableSearchParameterDefinitionManager, searchParameterExpressionParser);

            var searchOptionsFactory = new SearchOptionsFactory(
                expressionParser,
                () => searchableSearchParameterDefinitionManager,
                options,
                _fhirRequestContextAccessor,
                sqlSortingValidator,
                new ExpressionAccessControl(_fhirRequestContextAccessor),
                NullLogger<SearchOptionsFactory>.Instance);

            var searchParamTableExpressionQueryGeneratorFactory = new SearchParamTableExpressionQueryGeneratorFactory(searchParameterToSearchValueTypeMap);
            var sqlRootExpressionRewriter = new SqlRootExpressionRewriter(searchParamTableExpressionQueryGeneratorFactory);
            var chainFlatteningRewriter = new ChainFlatteningRewriter(searchParamTableExpressionQueryGeneratorFactory);
            var sortRewriter = new SortRewriter(searchParamTableExpressionQueryGeneratorFactory);
            var partitionEliminationRewriter = new PartitionEliminationRewriter(sqlServerFhirModel, SchemaInformation, () => searchableSearchParameterDefinitionManager);
            var compartmentDefinitionManager = new CompartmentDefinitionManager(ModelInfoProvider.Instance);
            compartmentDefinitionManager.StartAsync(CancellationToken.None).Wait();
            var compartmentSearchRewriter = new CompartmentSearchRewriter(new Lazy<ICompartmentDefinitionManager>(() => compartmentDefinitionManager), new Lazy<ISearchParameterDefinitionManager>(() => _searchParameterDefinitionManager));
            var smartCompartmentSearchRewriter = new SmartCompartmentSearchRewriter(compartmentSearchRewriter, new Lazy<ISearchParameterDefinitionManager>(() => _searchParameterDefinitionManager));

            SqlQueryHashCalculator = new TestSqlHashCalculator();

            _searchService = new SqlServerSearchService(
                searchOptionsFactory,
                _fhirDataStore,
                sqlServerFhirModel,
                sqlRootExpressionRewriter,
                chainFlatteningRewriter,
                sortRewriter,
                partitionEliminationRewriter,
                compartmentSearchRewriter,
                smartCompartmentSearchRewriter,
                SqlConnectionWrapperFactory,
                SqlConnectionBuilder,
                SqlRetryService,
                SqlServerDataStoreConfiguration,
                SchemaInformation,
                _fhirRequestContextAccessor,
                new CompressedRawResourceConverter(),
                SqlQueryHashCalculator,
                NullLogger<SqlServerSearchService>.Instance);

            ISearchParameterSupportResolver searchParameterSupportResolver = Substitute.For<ISearchParameterSupportResolver>();
            searchParameterSupportResolver.IsSearchParameterSupported(Arg.Any<SearchParameterInfo>()).Returns((true, false));

            _searchParameterStatusManager = new SearchParameterStatusManager(
                SqlServerSearchParameterStatusDataStore,
                _searchParameterDefinitionManager,
                searchParameterSupportResolver,
                mediator,
                NullLogger<SearchParameterStatusManager>.Instance);

            _testHelper = new SqlServerFhirStorageTestHelper(initialConnectionString, MasterDatabaseName, sqlServerFhirModel, SqlConnectionBuilder, queueClient);

            _sqlQueueClient = new SqlQueueClient(SqlConnectionWrapperFactory, SchemaInformation, SqlRetryService, NullLogger<SqlQueueClient>.Instance);
        }

        public string TestConnectionString { get; }

        internal SqlTransactionHandler SqlTransactionHandler { get; }

        internal SqlConnectionWrapperFactory SqlConnectionWrapperFactory { get; }

        internal SqlServerFhirDataStore SqlServerFhirDataStore => _fhirDataStore;

        internal IOptions<SqlServerDataStoreConfiguration> SqlServerDataStoreConfiguration { get; }

        internal ISqlConnectionBuilder SqlConnectionBuilder { get; }

        internal ISqlRetryService SqlRetryService { get; }

        internal SqlServerSearchParameterStatusDataStore SqlServerSearchParameterStatusDataStore { get; }

        internal SqlServerFhirModel SqlServerFhirModel { get; }

        internal SchemaInformation SchemaInformation { get; }

        internal ISqlQueryHashCalculator SqlQueryHashCalculator { get; }

        public async Task InitializeAsync()
        {
            await _testHelper.CreateAndInitializeDatabase(_databaseName, _maximumSupportedSchemaVersion, forceIncrementalSchemaUpgrade: false, _schemaInitializer, CancellationToken.None);
            await _searchParameterDefinitionManager.EnsureInitializedAsync(CancellationToken.None);
        }

        public async Task DisposeAsync()
        {
            await _testHelper.DeleteDatabase(_databaseName, CancellationToken.None);
        }

        protected SqlConnection GetSqlConnection(string connectionString)
        {
            var connectionBuilder = new SqlConnectionStringBuilder(connectionString);
            var result = new SqlConnection(connectionBuilder.ToString());
            return result;
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

            if (serviceType == typeof(SqlServerFhirOperationDataStore))
            {
                return _sqlServerFhirOperationDataStore;
            }

            if (serviceType == typeof(IFhirStorageTestHelper))
            {
                return _testHelper;
            }

            if (serviceType == typeof(ISqlServerFhirStorageTestHelper))
            {
                return _testHelper;
            }

            if (serviceType.IsInstanceOfType(this))
            {
                return this;
            }

            if (serviceType == typeof(ITransactionHandler))
            {
                return SqlTransactionHandler;
            }

            if (serviceType == typeof(ISearchParameterStatusDataStore))
            {
                return SqlServerSearchParameterStatusDataStore;
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

            if (serviceType == typeof(SchemaInitializer))
            {
                return _schemaInitializer;
            }

            if (serviceType == typeof(SchemaUpgradeRunner))
            {
                return _schemaUpgradeRunner;
            }

            if (serviceType == typeof(SearchParameterStatusManager))
            {
                return _searchParameterStatusManager;
            }

            if (serviceType == typeof(RequestContextAccessor<IFhirRequestContext>))
            {
                return _fhirRequestContextAccessor;
            }

            if (serviceType == typeof(IQueueClient))
            {
                return _sqlQueueClient;
            }

            if (serviceType == typeof(TestSqlHashCalculator))
            {
                return SqlQueryHashCalculator as TestSqlHashCalculator;
            }

            return null;
        }
    }
}
