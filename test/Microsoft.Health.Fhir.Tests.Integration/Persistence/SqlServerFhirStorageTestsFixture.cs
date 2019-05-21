// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data.SqlClient;
using System.Numerics;
using Hl7.Fhir.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Configs;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using NSubstitute;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    public class SqlServerFhirStorageTestsFixture : IServiceProvider, IDisposable
    {
        private readonly string _masterConnectionString;
        private readonly string _databaseName;
        private readonly IFhirDataStore _fhirDataStore;
        private readonly SqlServerFhirStorageTestHelper _testHelper;

        public SqlServerFhirStorageTestsFixture()
        {
            var initialConnectionString = Environment.GetEnvironmentVariable("SqlServer:ConnectionString") ?? LocalDatabase.DefaultConnectionString;
            _databaseName = $"FHIRINTEGRATIONTEST_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}_{BigInteger.Abs(new BigInteger(Guid.NewGuid().ToByteArray()))}";
            _masterConnectionString = new SqlConnectionStringBuilder(initialConnectionString) { InitialCatalog = "master" }.ToString();
            TestConnectionString = new SqlConnectionStringBuilder(initialConnectionString) { InitialCatalog = _databaseName }.ToString();

            using (var connection = new SqlConnection(_masterConnectionString))
            {
                connection.Open();

                using (SqlCommand command = connection.CreateCommand())
                {
                    command.CommandTimeout = 600;
                    command.CommandText = $"CREATE DATABASE {_databaseName}";
                    command.ExecuteNonQuery();
                }
            }

            var config = new SqlServerDataStoreConfiguration { ConnectionString = TestConnectionString, Initialize = true };

            var schemaUpgradeRunner = new SchemaUpgradeRunner(config);

            var schemaInformation = new SchemaInformation();

            var schemaInitializer = new SchemaInitializer(config, schemaUpgradeRunner, schemaInformation, NullLogger<SchemaInitializer>.Instance);
            schemaInitializer.Start();

            var searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            searchParameterDefinitionManager.AllSearchParameters.Returns(new SearchParameter[0]);

            var securityConfiguration = new SecurityConfiguration { PrincipalClaims = { "oid" } };

            var sqlServerFhirModel = new SqlServerFhirModel(config, schemaInformation, searchParameterDefinitionManager, Options.Create(securityConfiguration), NullLogger<SqlServerFhirModel>.Instance);

            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSqlServerTableRowParameterGenerators();
            serviceCollection.AddSingleton(sqlServerFhirModel);

            ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();

            var upsertResourceTvpGenerator = serviceProvider.GetRequiredService<V1.UpsertResourceTvpGenerator<ResourceMetadata>>();
            var searchParameterToSearchValueTypeMap = new SearchParameterToSearchValueTypeMap(searchParameterDefinitionManager);

            _fhirDataStore = new SqlServerFhirDataStore(config, sqlServerFhirModel, searchParameterToSearchValueTypeMap, upsertResourceTvpGenerator, NullLogger<SqlServerFhirDataStore>.Instance);
            _testHelper = new SqlServerFhirStorageTestHelper(TestConnectionString);
        }

        public string TestConnectionString { get; }

        public void Dispose()
        {
            using (var connection = new SqlConnection(_masterConnectionString))
            {
                connection.Open();
                SqlConnection.ClearAllPools();

                using (SqlCommand command = connection.CreateCommand())
                {
                    command.CommandTimeout = 600;
                    command.CommandText = $"DROP DATABASE IF EXISTS {_databaseName}";
                    command.ExecuteNonQuery();
                }
            }
        }

        object IServiceProvider.GetService(Type serviceType)
        {
            if (serviceType == typeof(IFhirDataStore))
            {
                return _fhirDataStore;
            }

            if (serviceType == typeof(IFhirStorageTestHelper))
            {
                return _testHelper;
            }

            if (serviceType.IsInstanceOfType(this))
            {
                return this;
            }

            return null;
        }
    }
}
