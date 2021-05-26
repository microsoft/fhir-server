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
        private const string LocalConnectionString = "server=(local);Integrated Security=true";
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

        public SqlServerFhirStorageTestsFixture()
            : this(SchemaVersionConstants.Max, $"FHIRINTEGRATIONTEST_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}_{BigInteger.Abs(new BigInteger(Guid.NewGuid().ToByteArray()))}")
        {
        }

        internal SqlServerFhirStorageTestsFixture(int maximumSupportedSchemaVersion, string databaseName)
        {
            var initialConnectionString = Environment.GetEnvironmentVariable("SqlServer:ConnectionString") ?? LocalConnectionString;

            _maximumSupportedSchemaVersion = maximumSupportedSchemaVersion;
            _databaseName = databaseName;
            TestConnectionString = new SqlConnectionStringBuilder(initialConnectionString) { InitialCatalog = _databaseName }.ToString();

            var schemaOptions = new SqlServerSchemaOptions { AutomaticUpdatesEnabled = true };
            var config = new SqlServerDataStoreConfiguration { ConnectionString = TestConnectionString, Initialize = true, SchemaOptions = schemaOptions };

            var schemaInformation = new SchemaInformation(SchemaVersionConstants.Min, maximumSupportedSchemaVersion);
            var scriptProvider = new ScriptProvider<SchemaVersion>();
            var baseScriptProvider = new BaseScriptProvider();
            var mediator = Substitute.For<IMediator>();

            var sqlConnectionStringProvider = new DefaultSqlConnectionStringProvider(config);
            var sqlConnectionFactory = new DefaultSqlConnectionFactory(sqlConnectionStringProvider);
            var schemaManagerDataStore = new SchemaManagerDataStore(sqlConnectionFactory);
            _schemaUpgradeRunner = new SchemaUpgradeRunner(scriptProvider, baseScriptProvider, NullLogger<SchemaUpgradeRunner>.Instance, sqlConnectionFactory, schemaManagerDataStore);
            _schemaInitializer = new SchemaInitializer(config, _schemaUpgradeRunner, schemaInformation, sqlConnectionFactory, sqlConnectionStringProvider, mediator, NullLogger<SchemaInitializer>.Instance);

            _searchParameterDefinitionManager = new SearchParameterDefinitionManager(ModelInfoProvider.Instance, _mediator, () => _searchService.CreateMockScope(), NullLogger<SearchParameterDefinitionManager>.Instance);

            _searchParameterDefinitionManager.StartAsync(CancellationToken.None);

            _filebasedSearchParameterStatusDataStore = new FilebasedSearchParameterStatusDataStore(_searchParameterDefinitionManager, ModelInfoProvider.Instance);

            var securityConfiguration = new SecurityConfiguration { PrincipalClaims = { "oid" } };

            var sqlServerFhirModel = new SqlServerFhirModel(
                schemaInformation,
                _searchParameterDefinitionManager,
                () => _filebasedSearchParameterStatusDataStore,
                Options.Create(securityConfiguration),
                sqlConnectionStringProvider,
                Substitute.For<IMediator>(),
                NullLogger<SqlServerFhirModel>.Instance);

            var searchParameterToSearchValueTypeMap = new SearchParameterToSearchValueTypeMap();

            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSqlServerTableRowParameterGenerators();
            serviceCollection.AddSingleton(sqlServerFhirModel);
            serviceCollection.AddSingleton<ISqlServerFhirModel>(sqlServerFhirModel);
            serviceCollection.AddSingleton(searchParameterToSearchValueTypeMap);

            ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();

            var upsertResourceTvpGeneratorV6 = serviceProvider.GetRequiredService<V6.UpsertResourceTvpGenerator<ResourceMetadata>>();
            var upsertResourceTvpGeneratorV7 = serviceProvider.GetRequiredService<V7.UpsertResourceTvpGenerator<ResourceMetadata>>();
            var upsertResourceTvpGeneratorVLatest = serviceProvider.GetRequiredService<VLatest.UpsertResourceTvpGenerator<IReadOnlyList<ResourceWrapper>>>();
            var upsertSearchParamsTvpGenerator = serviceProvider.GetRequiredService<VLatest.UpsertSearchParamsTvpGenerator<List<ResourceSearchParameterStatus>>>();
            var reindexResourceTvpGenerator = serviceProvider.GetRequiredService<VLatest.ReindexResourceTvpGenerator<IReadOnlyList<ResourceWrapper>>>();
            var bulkReindexResourceTvpGenerator = serviceProvider.GetRequiredService<VLatest.BulkReindexResourcesTvpGenerator<IReadOnlyList<ResourceWrapper>>>();

            _supportedSearchParameterDefinitionManager = new SupportedSearchParameterDefinitionManager(_searchParameterDefinitionManager);

            SqlTransactionHandler = new SqlTransactionHandler();
            SqlConnectionWrapperFactory = new SqlConnectionWrapperFactory(SqlTransactionHandler, new SqlCommandWrapperFactory(), sqlConnectionFactory);

            SqlServerSearchParameterStatusDataStore = new SqlServerSearchParameterStatusDataStore(
                () => SqlConnectionWrapperFactory.CreateMockScope(),
                upsertSearchParamsTvpGenerator,
                () => _filebasedSearchParameterStatusDataStore,
                schemaInformation,
                new SqlServerSortingValidator(schemaInformation),
                sqlServerFhirModel);

            IOptions<CoreFeatureConfiguration> options = Options.Create(new CoreFeatureConfiguration());

            _fhirDataStore = new SqlServerFhirDataStore(
                sqlServerFhirModel,
                searchParameterToSearchValueTypeMap,
                upsertResourceTvpGeneratorV6,
                upsertResourceTvpGeneratorV7,
                upsertResourceTvpGeneratorVLatest,
                reindexResourceTvpGenerator,
                bulkReindexResourceTvpGenerator,
                options,
                SqlConnectionWrapperFactory,
                NullLogger<SqlServerFhirDataStore>.Instance,
                schemaInformation);

            _fhirOperationDataStore = new SqlServerFhirOperationDataStore(SqlConnectionWrapperFactory, NullLogger<SqlServerFhirOperationDataStore>.Instance);

            var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            fhirRequestContextAccessor.RequestContext.CorrelationId.Returns(Guid.NewGuid().ToString());

            var searchableSearchParameterDefinitionManager = new SearchableSearchParameterDefinitionManager(_searchParameterDefinitionManager, fhirRequestContextAccessor);
            var searchParameterExpressionParser = new SearchParameterExpressionParser(new ReferenceSearchValueParser(fhirRequestContextAccessor));
            var expressionParser = new ExpressionParser(() => searchableSearchParameterDefinitionManager, searchParameterExpressionParser);

            var searchOptionsFactory = new SearchOptionsFactory(
                expressionParser,
                () => searchableSearchParameterDefinitionManager,
                options,
                fhirRequestContextAccessor,
                Substitute.For<ISortingValidator>(),
                NullLogger<SearchOptionsFactory>.Instance);

            var searchParamTableExpressionQueryGeneratorFactory = new SearchParamTableExpressionQueryGeneratorFactory(searchParameterToSearchValueTypeMap);
            var sqlRootExpressionRewriter = new SqlRootExpressionRewriter(searchParamTableExpressionQueryGeneratorFactory);
            var chainFlatteningRewriter = new ChainFlatteningRewriter(searchParamTableExpressionQueryGeneratorFactory);
            var sortRewriter = new SortRewriter(searchParamTableExpressionQueryGeneratorFactory);
            var partitionEliminationRewriter = new PartitionEliminationRewriter(sqlServerFhirModel, schemaInformation, () => searchableSearchParameterDefinitionManager);

            _searchService = new SqlServerSearchService(
                searchOptionsFactory,
                _fhirDataStore,
                sqlServerFhirModel,
                sqlRootExpressionRewriter,
                chainFlatteningRewriter,
                sortRewriter,
                partitionEliminationRewriter,
                SqlConnectionWrapperFactory,
                schemaInformation,
                fhirRequestContextAccessor,
                () => searchableSearchParameterDefinitionManager,
                NullLogger<SqlServerSearchService>.Instance);

            ISearchParameterSupportResolver searchParameterSupportResolver = Substitute.For<ISearchParameterSupportResolver>();
            searchParameterSupportResolver.IsSearchParameterSupported(Arg.Any<SearchParameterInfo>()).Returns((true, false));

            _searchParameterStatusManager = new SearchParameterStatusManager(
                SqlServerSearchParameterStatusDataStore,
                _searchParameterDefinitionManager,
                searchParameterSupportResolver,
                mediator,
                NullLogger<SearchParameterStatusManager>.Instance);

            _testHelper = new SqlServerFhirStorageTestHelper(initialConnectionString, MasterDatabaseName, sqlServerFhirModel, sqlConnectionFactory);
        }

        public string TestConnectionString { get; }

        internal SqlTransactionHandler SqlTransactionHandler { get; }

        internal SqlConnectionWrapperFactory SqlConnectionWrapperFactory { get; }

        internal SqlServerSearchParameterStatusDataStore SqlServerSearchParameterStatusDataStore { get; }

        public async Task InitializeAsync()
        {
            await _testHelper.CreateAndInitializeDatabase(_databaseName, _maximumSupportedSchemaVersion, forceIncrementalSchemaUpgrade: false, _schemaInitializer, CancellationToken.None);
            await _searchParameterDefinitionManager.EnsureInitializedAsync(CancellationToken.None);
        }

        public async Task DisposeAsync()
        {
            await _testHelper.DeleteDatabase(_databaseName, CancellationToken.None);
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

            return null;
        }
    }
}
