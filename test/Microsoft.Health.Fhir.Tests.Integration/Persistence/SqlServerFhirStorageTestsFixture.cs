// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data.SqlClient;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Configs;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using NSubstitute;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    public class SqlServerFhirStorageTestsFixture : IScoped<IFhirDataStore>
    {
        private readonly string _initialConnectionString;
        private readonly string _databaseName;

        public SqlServerFhirStorageTestsFixture()
        {
            _initialConnectionString = Environment.GetEnvironmentVariable("SqlServer:ConnectionString") ?? LocalDatabase.DefaultConnectionString;
            _databaseName = $"FHIRINTEGRATIONTEST_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

            using (var connection = new SqlConnection(_initialConnectionString))
            {
                connection.Open();

                using (SqlCommand command = connection.CreateCommand())
                {
                    command.CommandText = $"CREATE DATABASE {_databaseName}";
                    command.ExecuteNonQuery();
                }
            }

            TestConnectionString = new SqlConnectionStringBuilder(_initialConnectionString) { InitialCatalog = _databaseName }.ToString();
            var config = new SqlServerDataStoreConfiguration { ConnectionString = TestConnectionString, Initialize = true };

            var schemaUpgradeRunner = new SchemaUpgradeRunner(config);

            var schemaInformation = new SchemaInformation();

            var schemaInitializer = new SchemaInitializer(config, schemaUpgradeRunner, schemaInformation, NullLogger<SchemaInitializer>.Instance);
            schemaInitializer.Start();

            var searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            searchParameterDefinitionManager.AllSearchParameters.Returns(new SearchParameter[0]);

            var sqlServerFhirModel = new SqlServerFhirModel(config, searchParameterDefinitionManager, NullLogger<SqlServerFhirModel>.Instance);

            Value = new SqlServerFhirDataStore(config, sqlServerFhirModel, NullLogger<SqlServerFhirDataStore>.Instance);
        }

        public IFhirDataStore Value { get; }

        public string TestConnectionString { get; }

        public void Dispose()
        {
            using (var connection = new SqlConnection(_initialConnectionString))
            {
                connection.Open();
                SqlConnection.ClearAllPools();

                using (SqlCommand command = connection.CreateCommand())
                {
                    command.CommandText = $"DROP DATABASE IF EXISTS {_databaseName}";
                    command.ExecuteNonQuery();
                }
            }
        }

        private class MyStartup
        {
            public void ConfigureServices(IServiceCollection services)
            {
            }

            public void Configure(IApplicationBuilder app)
            {
            }
        }
    }
}
