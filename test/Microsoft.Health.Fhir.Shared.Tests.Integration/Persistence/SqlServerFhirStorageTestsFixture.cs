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
using Microsoft.Health.Fhir.Core.Features.Search;
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
        private readonly IFhirDataStore _fhirDataStore;
        private readonly IFhirOperationDataStore _fhirOperationDataStore;
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
            var config = Options.Create(new SqlServerDataStoreConfiguration { ConnectionString = TestConnectionString, Initialize = true, SchemaOptions = schemaOptions, StatementTimeout = TimeSpan.FromMinutes(10) });

            SchemaInformation = new SchemaInformation(SchemaVersionConstants.Min, maximumSupportedSchemaVersion);
            var scriptProvider = new ScriptProvider<SchemaVersion>();
            var baseScriptProvider = new BaseScriptProvider();
            var mediator = Substitute.For<IMediator>();
            var sqlSortingValidator = new SqlServerSortingValidator(SchemaInformation);
            SqlRetryLogicBaseProvider sqlRetryLogicBaseProvider = SqlConfigurableRetryFactory.CreateFixedRetryProvider(new SqlClientRetryOptions().Settings);

            var sqlConnectionStringProvider = new DefaultSqlConnectionStringProvider(config);
            SqlConnectionBuilder = new DefaultSqlConnectionBuilder(sqlConnectionStringProvider, sqlRetryLogicBaseProvider);

            var sqlConnection = Substitute.For<ISqlConnectionBuilder>();
            sqlConnection.GetSqlConnectionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ReturnsForAnyArgs((x) => Task.FromResult(GetSqlConnection(TestConnectionString)));
            var sqlConnectionWrapperFactory = new SqlConnectionWrapperFactory(new SqlTransactionHandler(), sqlConnection, sqlRetryLogicBaseProvider, config);
            var schemaManagerDataStore = new SchemaManagerDataStore(sqlConnectionWrapperFactory, config, NullLogger<SchemaManagerDataStore>.Instance);
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
            _schemaInitializer = new SchemaInitializer(serviceProviderSchemaInitializer, config, SchemaInformation, mediator, NullLogger<SchemaInitializer>.Instance);

            _searchParameterDefinitionManager = new SearchParameterDefinitionManager(ModelInfoProvider.Instance, _mediator, () => _searchService.CreateMockScope(), NullLogger<SearchParameterDefinitionManager>.Instance);

            _searchParameterDefinitionManager.StartAsync(CancellationToken.None);

            _filebasedSearchParameterStatusDataStore = new FilebasedSearchParameterStatusDataStore(_searchParameterDefinitionManager, ModelInfoProvider.Instance);

            var securityConfiguration = new SecurityConfiguration { PrincipalClaims = { "oid" } };

            SqlTransactionHandler = new SqlTransactionHandler();
            SqlConnectionWrapperFactory = new SqlConnectionWrapperFactory(SqlTransactionHandler, SqlConnectionBuilder, sqlRetryLogicBaseProvider, config);

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

            ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();

            var upsertResourceTvpGeneratorV6 = serviceProvider.GetRequiredService<V6.UpsertResourceTvpGenerator<ResourceMetadata>>();
            var upsertResourceTvpGeneratorV7 = serviceProvider.GetRequiredService<V7.UpsertResourceTvpGenerator<ResourceMetadata>>();
            var upsertResourceTvpGeneratorV13 = serviceProvider.GetRequiredService<V13.UpsertResourceTvpGenerator<IReadOnlyList<ResourceWrapper>>>();
            var upsertResourceTvpGeneratorV17 = serviceProvider.GetRequiredService<V17.UpsertResourceTvpGenerator<IReadOnlyList<ResourceWrapper>>>();
            var upsertResourceTvpGeneratorV18 = serviceProvider.GetRequiredService<V18.UpsertResourceTvpGenerator<IReadOnlyList<ResourceWrapper>>>();
            var upsertResourceTvpGeneratorV27 = serviceProvider.GetRequiredService<V27.UpsertResourceTvpGenerator<IReadOnlyList<ResourceWrapper>>>();
            var upsertResourceTvpGeneratorVLatest = serviceProvider.GetRequiredService<VLatest.UpsertResourceTvpGenerator<IReadOnlyList<ResourceWrapper>>>();
            var reindexResourceTvpGeneratorV17 = serviceProvider.GetRequiredService<V17.ReindexResourceTvpGenerator<IReadOnlyList<ResourceWrapper>>>();
            var bulkReindexResourceTvpGeneratorV17 = serviceProvider.GetRequiredService<V17.BulkReindexResourcesTvpGenerator<IReadOnlyList<ResourceWrapper>>>();
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

            _fhirDataStore = new SqlServerFhirDataStore(
                sqlServerFhirModel,
                searchParameterToSearchValueTypeMap,
                upsertResourceTvpGeneratorV6,
                upsertResourceTvpGeneratorV7,
                upsertResourceTvpGeneratorV13,
                upsertResourceTvpGeneratorV17,
                upsertResourceTvpGeneratorV18,
                upsertResourceTvpGeneratorV27,
                upsertResourceTvpGeneratorVLatest,
                reindexResourceTvpGeneratorV17,
                reindexResourceTvpGeneratorVLatest,
                bulkReindexResourceTvpGeneratorV17,
                bulkReindexResourceTvpGeneratorVLatest,
                options,
                SqlConnectionWrapperFactory,
                new CompressedRawResourceConverter(),
                NullLogger<SqlServerFhirDataStore>.Instance,
                SchemaInformation,
                ModelInfoProvider.Instance,
                _fhirRequestContextAccessor,
                config);

            _fhirOperationDataStore = new SqlServerFhirOperationDataStore(SqlConnectionWrapperFactory, SchemaInformation, NullLogger<SqlServerFhirOperationDataStore>.Instance);

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
                NullLogger<SearchOptionsFactory>.Instance);

            var searchParamTableExpressionQueryGeneratorFactory = new SearchParamTableExpressionQueryGeneratorFactory(searchParameterToSearchValueTypeMap);
            var sqlRootExpressionRewriter = new SqlRootExpressionRewriter(searchParamTableExpressionQueryGeneratorFactory);
            var chainFlatteningRewriter = new ChainFlatteningRewriter(searchParamTableExpressionQueryGeneratorFactory);
            var sortRewriter = new SortRewriter(searchParamTableExpressionQueryGeneratorFactory);
            var partitionEliminationRewriter = new PartitionEliminationRewriter(sqlServerFhirModel, SchemaInformation, () => searchableSearchParameterDefinitionManager);
            var compartmentDefinitionManager = new CompartmentDefinitionManager(ModelInfoProvider.Instance);
            var compartmentSearchRewriter = new CompartmentSearchRewriter(new Lazy<ICompartmentDefinitionManager>(() => compartmentDefinitionManager), new Lazy<ISearchParameterDefinitionManager>(() => _searchParameterDefinitionManager));

            _searchService = new SqlServerSearchService(
                searchOptionsFactory,
                _fhirDataStore,
                sqlServerFhirModel,
                sqlRootExpressionRewriter,
                chainFlatteningRewriter,
                sortRewriter,
                partitionEliminationRewriter,
                compartmentSearchRewriter,
                SqlConnectionWrapperFactory,
                SchemaInformation,
                _fhirRequestContextAccessor,
                new CompressedRawResourceConverter(),
                NullLogger<SqlServerSearchService>.Instance);

            ISearchParameterSupportResolver searchParameterSupportResolver = Substitute.For<ISearchParameterSupportResolver>();
            searchParameterSupportResolver.IsSearchParameterSupported(Arg.Any<SearchParameterInfo>()).Returns((true, false));

            _searchParameterStatusManager = new SearchParameterStatusManager(
                SqlServerSearchParameterStatusDataStore,
                _searchParameterDefinitionManager,
                searchParameterSupportResolver,
                mediator,
                NullLogger<SearchParameterStatusManager>.Instance);

            _testHelper = new SqlServerFhirStorageTestHelper(initialConnectionString, MasterDatabaseName, sqlServerFhirModel, SqlConnectionBuilder);
        }

        public string TestConnectionString { get; }

        internal SqlTransactionHandler SqlTransactionHandler { get; }

        internal SqlConnectionWrapperFactory SqlConnectionWrapperFactory { get; }

        internal ISqlConnectionBuilder SqlConnectionBuilder { get; }

        internal SqlServerSearchParameterStatusDataStore SqlServerSearchParameterStatusDataStore { get; }

        internal SqlServerFhirModel SqlServerFhirModel { get; }

        internal SchemaInformation SchemaInformation { get; }

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

            return null;
        }
    }
}
