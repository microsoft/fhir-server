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
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions.Parsers;
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
        private readonly FilebasedSearchParameterStatusDataStore _filebasedSearchParameterStatusDataStore;
        private readonly ISearchService _searchService;
        private readonly SupportedSearchParameterDefinitionManager _supportedSearchParameterDefinitionManager;

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

            var sqlConnectionFactory = new DefaultSqlConnectionFactory(config);
            var schemaUpgradeRunner = new SchemaUpgradeRunner(scriptProvider, baseScriptProvider, mediator, NullLogger<SchemaUpgradeRunner>.Instance, sqlConnectionFactory);
            _schemaInitializer = new SchemaInitializer(config, schemaUpgradeRunner, schemaInformation, sqlConnectionFactory, NullLogger<SchemaInitializer>.Instance);

            SearchParameterDefinitionManager = new SearchParameterDefinitionManager(ModelInfoProvider.Instance);

            _filebasedSearchParameterStatusDataStore = new FilebasedSearchParameterStatusDataStore(SearchParameterDefinitionManager, ModelInfoProvider.Instance);

            var securityConfiguration = new SecurityConfiguration { PrincipalClaims = { "oid" } };

            var sqlServerFhirModel = new SqlServerFhirModel(
                config,
                schemaInformation,
                SearchParameterDefinitionManager,
                () => _filebasedSearchParameterStatusDataStore,
                Options.Create(securityConfiguration),
                NullLogger<SqlServerFhirModel>.Instance);

            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSqlServerTableRowParameterGenerators();
            serviceCollection.AddSingleton(sqlServerFhirModel);

            ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();

            var upsertResourceTvpGeneratorV6 = serviceProvider.GetRequiredService<V6.UpsertResourceTvpGenerator<ResourceMetadata>>();
            var upsertResourceTvpGeneratorV7 = serviceProvider.GetRequiredService<V7.UpsertResourceTvpGenerator<ResourceMetadata>>();
            var upsertResourceTvpGeneratorVLatest = serviceProvider.GetRequiredService<VLatest.UpsertResourceTvpGenerator<ResourceMetadata>>();
            var upsertSearchParamsTvpGenerator = serviceProvider.GetRequiredService<VLatest.UpsertSearchParamsTvpGenerator<List<ResourceSearchParameterStatus>>>();

            _supportedSearchParameterDefinitionManager = new SupportedSearchParameterDefinitionManager(SearchParameterDefinitionManager);
            var searchParameterToSearchValueTypeMap = new SearchParameterToSearchValueTypeMap();

            SqlTransactionHandler = new SqlTransactionHandler();
            SqlConnectionWrapperFactory = new SqlConnectionWrapperFactory(SqlTransactionHandler, new SqlCommandWrapperFactory(), sqlConnectionFactory);

            SqlServerSearchParameterStatusDataStore = new SqlServerSearchParameterStatusDataStore(
                () => SqlConnectionWrapperFactory.CreateMockScope(),
                upsertSearchParamsTvpGenerator,
                () => _filebasedSearchParameterStatusDataStore,
                schemaInformation,
                sqlServerFhirModel);

            IOptions<CoreFeatureConfiguration> options = Options.Create(new CoreFeatureConfiguration());

            _fhirDataStore = new SqlServerFhirDataStore(
                sqlServerFhirModel,
                searchParameterToSearchValueTypeMap,
                upsertResourceTvpGeneratorV6,
                upsertResourceTvpGeneratorV7,
                upsertResourceTvpGeneratorVLatest,
                options,
                SqlConnectionWrapperFactory,
                NullLogger<SqlServerFhirDataStore>.Instance,
                schemaInformation);

            _fhirOperationDataStore = new SqlServerFhirOperationDataStore(SqlConnectionWrapperFactory, NullLogger<SqlServerFhirOperationDataStore>.Instance);

            var fhirRequestContextAccessor = Substitute.For<IFhirRequestContextAccessor>();
            fhirRequestContextAccessor.FhirRequestContext.CorrelationId.Returns(Guid.NewGuid().ToString());

            var searchableSearchParameterDefinitionManager = new SearchableSearchParameterDefinitionManager(SearchParameterDefinitionManager, fhirRequestContextAccessor);
            var searchParameterExpressionParser = new SearchParameterExpressionParser(new ReferenceSearchValueParser(fhirRequestContextAccessor));
            var expressionParser = new ExpressionParser(() => searchableSearchParameterDefinitionManager, searchParameterExpressionParser);

            SearchParameterDefinitionManager.StartAsync(CancellationToken.None);

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

            _searchService = new SqlServerSearchService(
                searchOptionsFactory,
                _fhirDataStore,
                sqlServerFhirModel,
                sqlRootExpressionRewriter,
                chainFlatteningRewriter,
                sortRewriter,
                SqlConnectionWrapperFactory,
                schemaInformation,
                new SqlServerSortingValidator(),
                fhirRequestContextAccessor,
                NullLogger<SqlServerSearchService>.Instance);

            _testHelper = new SqlServerFhirStorageTestHelper(initialConnectionString, MasterDatabaseName, sqlServerFhirModel, sqlConnectionFactory);
        }

        public string TestConnectionString { get; }

        internal SqlTransactionHandler SqlTransactionHandler { get; }

        internal SqlConnectionWrapperFactory SqlConnectionWrapperFactory { get; }

        internal SearchParameterDefinitionManager SearchParameterDefinitionManager { get; }

        internal SqlServerSearchParameterStatusDataStore SqlServerSearchParameterStatusDataStore { get; }

        public async Task InitializeAsync()
        {
            await _testHelper.CreateAndInitializeDatabase(_databaseName, _maximumSupportedSchemaVersion, forceIncrementalSchemaUpgrade: false, _schemaInitializer, CancellationToken.None);
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
                return SearchParameterDefinitionManager;
            }

            if (serviceType == typeof(SupportedSearchParameterDefinitionManager))
            {
                return _supportedSearchParameterDefinitionManager;
            }

            return null;
        }
    }
}
