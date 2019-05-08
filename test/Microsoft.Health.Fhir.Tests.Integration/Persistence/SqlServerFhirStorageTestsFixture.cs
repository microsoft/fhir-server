// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data.SqlClient;
using System.Numerics;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Configs;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    public class SqlServerFhirStorageTestsFixture : IServiceProvider, IAsyncLifetime
    {
        private readonly string _initialConnectionString;
        private readonly string _databaseName;
        private IFhirDataStore _fhirDataStore;
        private SqlServerFhirStorageTestHelper _testHelper;

        public SqlServerFhirStorageTestsFixture()
        {
            _initialConnectionString = Environment.GetEnvironmentVariable("SqlServer:ConnectionString") ?? LocalDatabase.DefaultConnectionString;
            _databaseName = $"FHIRINTEGRATIONTEST_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}_{BigInteger.Abs(new BigInteger(Guid.NewGuid().ToByteArray()))}";
            TestConnectionString = new SqlConnectionStringBuilder(_initialConnectionString) { InitialCatalog = _databaseName }.ToString();
        }

        public string TestConnectionString { get; }

        async Task IAsyncLifetime.InitializeAsync()
        {
            using (var connection = new SqlConnection(_initialConnectionString))
            {
                await connection.OpenAsync();

                using (SqlCommand command = connection.CreateCommand())
                {
                    command.CommandText = $"CREATE DATABASE {_databaseName}";
                    await command.ExecuteNonQueryAsync();
                }
            }

            var config = new SqlServerDataStoreConfiguration { ConnectionString = TestConnectionString, Initialize = true };

            var schemaUpgradeRunner = new SchemaUpgradeRunner(config);

            var schemaInformation = new SchemaInformation();

            var schemaInitializer = new SchemaInitializer(config, schemaUpgradeRunner, schemaInformation, NullLogger<SchemaInitializer>.Instance);
            schemaInitializer.Start();

            var searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            searchParameterDefinitionManager.AllSearchParameters.Returns(new SearchParameter[0]);

            var securityConfiguration = new SecurityConfiguration { LastModifiedClaims = { "oid" } };

            var sqlServerFhirModel = new SqlServerFhirModel(config, schemaInformation, searchParameterDefinitionManager, Options.Create(securityConfiguration), NullLogger<SqlServerFhirModel>.Instance);

            _fhirDataStore = new SqlServerFhirDataStore(config, sqlServerFhirModel, NullLogger<SqlServerFhirDataStore>.Instance);
            _testHelper = new SqlServerFhirStorageTestHelper(TestConnectionString);
        }

        async Task IAsyncLifetime.DisposeAsync()
        {
            using (var connection = new SqlConnection(_initialConnectionString))
            {
                await connection.OpenAsync();
                SqlConnection.ClearAllPools();

                using (SqlCommand command = connection.CreateCommand())
                {
                    command.CommandText = $"DROP DATABASE IF EXISTS {_databaseName}";
                    await command.ExecuteNonQueryAsync();
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
