// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data.SqlClient;
using System.Numerics;
using System.Threading;
using Hl7.Fhir.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Configs;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    public class SqlServerFhirStorageTestsFixture : IServiceProvider, IAsyncLifetime
    {
        private const string LocalConnectionString = "server=(local);Integrated Security=true";

        private readonly string _databaseName;
        private readonly IFhirDataStore _fhirDataStore;
        private readonly IFhirOperationDataStore _fhirOperationDataStore;
        private readonly SqlServerFhirStorageTestHelper _testHelper;
        private readonly SchemaInitializer _schemaInitializer;

        public SqlServerFhirStorageTestsFixture()
        {
            var initialConnectionString = Environment.GetEnvironmentVariable("SqlServer:ConnectionString") ?? LocalConnectionString;

            _databaseName = $"FHIRINTEGRATIONTEST_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}_{BigInteger.Abs(new BigInteger(Guid.NewGuid().ToByteArray()))}";
            string masterConnectionString = new SqlConnectionStringBuilder(initialConnectionString) { InitialCatalog = "master" }.ToString();
            TestConnectionString = new SqlConnectionStringBuilder(initialConnectionString) { InitialCatalog = _databaseName }.ToString();

            SqlServerDataStoreConfiguration = new SqlServerDataStoreConfiguration { ConnectionString = TestConnectionString, Initialize = true };
            var sqlConnectionWrapperFactory = new SqlConnectionWrapperFactory(SqlServerDataStoreConfiguration, new SqlTransactionHandler());

            var schemaUpgradeRunner = new SchemaUpgradeRunner(SqlServerDataStoreConfiguration, NullLogger<SchemaUpgradeRunner>.Instance);
            var instanceSchemaHelper = new InstanceSchemaDataStore(SqlServerDataStoreConfiguration, NullLogger<InstanceSchemaDataStore>.Instance);

            var schemaInformation = new SchemaInformation();

            _schemaInitializer = new SchemaInitializer(SqlServerDataStoreConfiguration, schemaUpgradeRunner, schemaInformation, instanceSchemaHelper, NullLogger<SchemaInitializer>.Instance);

            var searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            searchParameterDefinitionManager.AllSearchParameters.Returns(new[]
            {
                new SearchParameter { Name = SearchParameterNames.Id, Type = SearchParamType.Token, Url = SearchParameterNames.IdUri.ToString() }.ToInfo(),
                new SearchParameter { Name = SearchParameterNames.LastUpdated, Type = SearchParamType.Date, Url = SearchParameterNames.LastUpdatedUri.ToString() }.ToInfo(),
            });

            var securityConfiguration = new SecurityConfiguration { PrincipalClaims = { "oid" } };

            var sqlServerFhirModel = new SqlServerFhirModel(SqlServerDataStoreConfiguration, schemaInformation, () => searchParameterDefinitionManager, Options.Create(securityConfiguration), NullLogger<SqlServerFhirModel>.Instance);

            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSqlServerTableRowParameterGenerators();
            serviceCollection.AddSingleton(sqlServerFhirModel);

            ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();

            var upsertResourceTvpGenerator = serviceProvider.GetRequiredService<VLatest.UpsertResourceTvpGenerator<ResourceMetadata>>();
            var searchParameterToSearchValueTypeMap = new SearchParameterToSearchValueTypeMap(() => searchParameterDefinitionManager);

            SqlTransactionHandler = new SqlTransactionHandler();
            SqlConnectionWrapperFactory = new SqlConnectionWrapperFactory(SqlServerDataStoreConfiguration, SqlTransactionHandler);

            _fhirDataStore = new SqlServerFhirDataStore(SqlServerDataStoreConfiguration, sqlServerFhirModel, searchParameterToSearchValueTypeMap, upsertResourceTvpGenerator, Options.Create(new CoreFeatureConfiguration()), SqlConnectionWrapperFactory, NullLogger<SqlServerFhirDataStore>.Instance);

            _fhirOperationDataStore = new SqlServerFhirOperationDataStore(sqlConnectionWrapperFactory, NullLogger<SqlServerFhirOperationDataStore>.Instance);

            _testHelper = new SqlServerFhirStorageTestHelper(TestConnectionString, initialConnectionString, masterConnectionString);
        }

        public string TestConnectionString { get; }

        internal SqlTransactionHandler SqlTransactionHandler { get; }

        internal SqlConnectionWrapperFactory SqlConnectionWrapperFactory { get; }

        public SqlServerDataStoreConfiguration SqlServerDataStoreConfiguration { get; }

        public async Task InitializeAsync()
        {
            await _testHelper.CreateAndInitializeDatabase(_databaseName, forceIncrementalSchemaUpgrade: false, _schemaInitializer, CancellationToken.None);
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

            return null;
        }
    }
}
