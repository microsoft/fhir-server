// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using Azure.Identity;
using MediatR;
using Microsoft.AspNetCore.Components.Forms;
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

        private readonly string _initialConnectionString;
        private readonly IOptions<CoreFeatureConfiguration> _options;
        private readonly int _maximumSupportedSchemaVersion;
        private readonly string _databaseName;
        private readonly IMediator _mediator = Substitute.For<IMediator>();
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

        private SqlServerFhirDataStore _fhirDataStore;
        private IFhirOperationDataStore _fhirOperationDataStore;
        private SqlServerFhirOperationDataStore _sqlServerFhirOperationDataStore;
        private SqlServerFhirStorageTestHelper _testHelper;
        private SchemaInitializer _schemaInitializer;
        private SchemaUpgradeRunner _schemaUpgradeRunner;
        private FilebasedSearchParameterStatusDataStore _filebasedSearchParameterStatusDataStore;
        private ISearchService _searchService;
        private SearchParameterDefinitionManager _searchParameterDefinitionManager;
        private SupportedSearchParameterDefinitionManager _supportedSearchParameterDefinitionManager;
        private SearchParameterStatusManager _searchParameterStatusManager;
        private SqlQueueClient _sqlQueueClient;

        public SqlServerFhirStorageTestsFixture()
            : this(SchemaVersionConstants.Max, GetDatabaseName())
        {
        }

        internal SqlServerFhirStorageTestsFixture(int maximumSupportedSchemaVersion, string databaseName, IOptions<CoreFeatureConfiguration> coreFeatures = null)
        {
            _initialConnectionString = Environment.GetEnvironmentVariable("SqlServer:ConnectionString") ?? LocalConnectionString;

            _maximumSupportedSchemaVersion = maximumSupportedSchemaVersion;
            _databaseName = databaseName;
            TestConnectionString = new SqlConnectionStringBuilder(_initialConnectionString) { InitialCatalog = _databaseName, Encrypt = true }.ToString();

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

            _options = coreFeatures ?? Options.Create(new CoreFeatureConfiguration());
        }

        public string TestConnectionString { get; private set; }

        internal SqlTransactionHandler SqlTransactionHandler { get; private set; }

        internal SqlConnectionWrapperFactory SqlConnectionWrapperFactory { get; private set; }

        internal SqlServerFhirDataStore SqlServerFhirDataStore => _fhirDataStore;

        internal IOptions<SqlServerDataStoreConfiguration> SqlServerDataStoreConfiguration { get; private set; }

        internal ISqlConnectionBuilder SqlConnectionBuilder { get; private set; }

        internal SqlRetryService SqlRetryService { get; private set; }

        internal SqlServerSearchParameterStatusDataStore SqlServerSearchParameterStatusDataStore { get; private set; }

        internal SqlServerFhirModel SqlServerFhirModel { get; private set; }

        internal SchemaInformation SchemaInformation { get; private set; }

        internal ISqlQueryHashCalculator SqlQueryHashCalculator { get; private set; }

        internal static string GetDatabaseName(string test = null)
        {
            return $"{ModelInfoProvider.Version}{(test == null ? string.Empty : $"_{test}")}_{DateTimeOffset.UtcNow.ToString("s").Replace("-", string.Empty).Replace(":", string.Empty)}_{Guid.NewGuid().ToString().Replace("-", string.Empty)}";
        }

        public async Task InitializeAsync()
        {
            var scriptProvider = new ScriptProvider<SchemaVersion>();
            var baseScriptProvider = new BaseScriptProvider();
            var mediator = Substitute.For<IMediator>();
            var sqlSortingValidator = new SqlServerSortingValidator(SchemaInformation);
            SqlRetryLogicBaseProvider sqlRetryLogicBaseProvider = SqlConfigurableRetryFactory.CreateFixedRetryProvider(new SqlClientRetryOptions().Settings);

            SqlConnectionBuilder = new DefaultSqlConnectionBuilder(SqlServerDataStoreConfiguration, sqlRetryLogicBaseProvider);

            var sqlConnection = Substitute.For<ISqlConnectionBuilder>();

            sqlConnection.GetSqlConnectionAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>()).ReturnsForAnyArgs((x) => Task.FromResult(GetSqlConnection(TestConnectionString)));
            var sqlConnectionWrapperFactory = new SqlConnectionWrapperFactory(new SqlTransactionHandler(), SqlConnectionBuilder, sqlRetryLogicBaseProvider, SqlServerDataStoreConfiguration);
            var schemaManagerDataStore = new SchemaManagerDataStore(sqlConnectionWrapperFactory, SqlServerDataStoreConfiguration, NullLogger<SchemaManagerDataStore>.Instance);
            _schemaUpgradeRunner = new SchemaUpgradeRunner(scriptProvider, baseScriptProvider, NullLogger<SchemaUpgradeRunner>.Instance, sqlConnectionWrapperFactory, schemaManagerDataStore);

            Func<IServiceProvider, SchemaUpgradeRunner> schemaUpgradeRunnerFactory = p => _schemaUpgradeRunner;
            Func<IServiceProvider, IReadOnlySchemaManagerDataStore> schemaManagerDataStoreFactory = p => schemaManagerDataStore;
            Func<IServiceProvider, SqlConnectionWrapperFactory> sqlConnectionWrapperFactoryFunc = p => sqlConnectionWrapperFactory;

            var collection = new ServiceCollection();
            collection.AddScoped(sqlConnectionWrapperFactoryFunc);
            collection.AddScoped(schemaUpgradeRunnerFactory);
            collection.AddScoped(schemaManagerDataStoreFactory);
            var serviceProviderSchemaInitializer = collection.BuildServiceProvider();
            _schemaInitializer = new SchemaInitializer(serviceProviderSchemaInitializer, SqlServerDataStoreConfiguration, SchemaInformation, mediator, NullLogger<SchemaInitializer>.Instance);

            _searchParameterDefinitionManager = new SearchParameterDefinitionManager(ModelInfoProvider.Instance, _mediator, CreateMockedScopeExtensions.CreateMockScopeProvider(() => _searchService), NullLogger<SearchParameterDefinitionManager>.Instance);

            _filebasedSearchParameterStatusDataStore = new FilebasedSearchParameterStatusDataStore(_searchParameterDefinitionManager, ModelInfoProvider.Instance);

            var securityConfiguration = new SecurityConfiguration { PrincipalClaims = { "oid" } };

            SqlTransactionHandler = new SqlTransactionHandler();
            SqlConnectionWrapperFactory = new SqlConnectionWrapperFactory(SqlTransactionHandler, SqlConnectionBuilder, sqlRetryLogicBaseProvider, SqlServerDataStoreConfiguration);

            var sqlServerFhirModel = new SqlServerFhirModel(
                SchemaInformation,
                _searchParameterDefinitionManager,
                () => _filebasedSearchParameterStatusDataStore,
                Options.Create(securityConfiguration),
                SqlConnectionWrapperFactory.CreateMockScopeProvider(),
                Substitute.For<IMediator>(),
                NullLogger<SqlServerFhirModel>.Instance);
            SqlServerFhirModel = sqlServerFhirModel;

            // the test queue client may not be enough for these tests. will need to look back into this.
            var queueClient = new TestQueueClient();

            // Add custom logic to set up the AzurePipelinesCredential if we are running in Azure Pipelines
            string federatedClientID = Environment.GetEnvironmentVariable("AZURESUBSCRIPTION_CLIENT_ID");
            string federatedTenantId = Environment.GetEnvironmentVariable("AZURESUBSCRIPTION_TENANT_ID");
            string serviceConnectionId = Environment.GetEnvironmentVariable("AZURESUBSCRIPTION_SERVICE_CONNECTION_ID");
            string systemAccessToken = Environment.GetEnvironmentVariable("SYSTEM_ACCESSTOKEN");

            if (!string.IsNullOrEmpty(federatedClientID) && !string.IsNullOrEmpty(federatedTenantId) && !string.IsNullOrEmpty(serviceConnectionId) && !string.IsNullOrEmpty(systemAccessToken))
            {
                AzurePipelinesCredential azurePipelinesCredential = new(federatedTenantId, federatedClientID, serviceConnectionId, systemAccessToken);
                SqlAuthenticationProvider.SetProvider(SqlAuthenticationMethod.ActiveDirectoryWorkloadIdentity, new SqlAzurePipelinesWorkloadIdentityAuthenticationProvider(azurePipelinesCredential));
            }

            _testHelper = new SqlServerFhirStorageTestHelper(_initialConnectionString, MasterDatabaseName, sqlServerFhirModel, SqlConnectionBuilder, queueClient);
            await _testHelper.CreateAndInitializeDatabase(_databaseName, _maximumSupportedSchemaVersion, forceIncrementalSchemaUpgrade: false, _schemaInitializer, CancellationToken.None);

            var searchParameterToSearchValueTypeMap = new SearchParameterToSearchValueTypeMap();

            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSqlServerTableRowParameterGenerators();
            serviceCollection.AddSingleton(sqlServerFhirModel);
            serviceCollection.AddSingleton<ISqlServerFhirModel>(sqlServerFhirModel);
            serviceCollection.AddSingleton(searchParameterToSearchValueTypeMap);
            var converter = (ICompressedRawResourceConverter)new CompressedRawResourceConverter();
            serviceCollection.AddSingleton(converter);

            ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();

            var upsertSearchParamsTvpGenerator = serviceProvider.GetRequiredService<VLatest.UpsertSearchParamsTvpGenerator<List<ResourceSearchParameterStatus>>>();

            _supportedSearchParameterDefinitionManager = new SupportedSearchParameterDefinitionManager(_searchParameterDefinitionManager);

            SqlServerSearchParameterStatusDataStore = new SqlServerSearchParameterStatusDataStore(
                SqlConnectionWrapperFactory.CreateMockScopeProvider(),
                upsertSearchParamsTvpGenerator,
                () => _filebasedSearchParameterStatusDataStore,
                SchemaInformation,
                sqlSortingValidator,
                sqlServerFhirModel,
                _searchParameterDefinitionManager);

            var bundleConfiguration = new BundleConfiguration() { SupportsBundleOrchestrator = true };
            var bundleOptions = Substitute.For<IOptions<BundleConfiguration>>();
            bundleOptions.Value.Returns(bundleConfiguration);

            var bundleOrchestrator = new BundleOrchestrator(bundleOptions, NullLogger<BundleOrchestrator>.Instance);

            SqlRetryService = new SqlRetryService(SqlConnectionBuilder, SqlServerDataStoreConfiguration, Options.Create(new SqlRetryServiceOptions()), new SqlRetryServiceDelegateOptions());

            var importErrorSerializer = new Shared.Core.Features.Operations.Import.ImportErrorSerializer(new Hl7.Fhir.Serialization.FhirJsonSerializer());

            _fhirDataStore = new SqlServerFhirDataStore(
                sqlServerFhirModel,
                searchParameterToSearchValueTypeMap,
                _options,
                bundleOrchestrator,
                SqlRetryService,
                SqlConnectionWrapperFactory,
                converter,
                NullLogger<SqlServerFhirDataStore>.Instance,
                SchemaInformation,
                ModelInfoProvider.Instance,
                _fhirRequestContextAccessor,
                importErrorSerializer,
                new SqlStoreClient(SqlRetryService, NullLogger<SqlStoreClient>.Instance));

            _fhirOperationDataStore = new SqlServerFhirOperationDataStore(SqlConnectionWrapperFactory, queueClient, NullLogger<SqlServerFhirOperationDataStore>.Instance, NullLoggerFactory.Instance);

            var sqlQueueClient = new SqlQueueClient(SchemaInformation, SqlRetryService, NullLogger<SqlQueueClient>.Instance);
            _sqlServerFhirOperationDataStore = new SqlServerFhirOperationDataStore(SqlConnectionWrapperFactory, sqlQueueClient, NullLogger<SqlServerFhirOperationDataStore>.Instance, NullLoggerFactory.Instance);

            _fhirRequestContextAccessor.RequestContext.CorrelationId.Returns(Guid.NewGuid().ToString());
            _fhirRequestContextAccessor.RequestContext.RouteName.Returns("routeName");

            var searchableSearchParameterDefinitionManager = new SearchableSearchParameterDefinitionManager(_searchParameterDefinitionManager, _fhirRequestContextAccessor);
            var searchParameterExpressionParser = new SearchParameterExpressionParser(new ReferenceSearchValueParser(_fhirRequestContextAccessor));
            var expressionParser = new ExpressionParser(() => searchableSearchParameterDefinitionManager, searchParameterExpressionParser);

            var searchOptionsFactory = new SearchOptionsFactory(
                expressionParser,
                () => searchableSearchParameterDefinitionManager,
                _options,
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

            _sqlQueueClient = new SqlQueueClient(SchemaInformation, SqlRetryService, NullLogger<SqlQueueClient>.Instance);

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
