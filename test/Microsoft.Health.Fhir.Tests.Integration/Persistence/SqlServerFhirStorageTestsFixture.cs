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
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Configs;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using NSubstitute;
using Polly;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    public class SqlServerFhirStorageTestsFixture : IServiceProvider, IAsyncLifetime
    {
        private const string LocalConnectionString = "server=(local);Integrated Security=true";

        private readonly string _masterConnectionString;
        private readonly string _databaseName;
        private readonly IFhirDataStore _fhirDataStore;
        private readonly SqlServerFhirStorageTestHelper _testHelper;
        private SchemaInitializer _schemaInitializer;

        public SqlServerFhirStorageTestsFixture()
        {
            var initialConnectionString = Environment.GetEnvironmentVariable("SqlServer:ConnectionString") ?? LocalConnectionString;

            _databaseName = $"FHIRINTEGRATIONTEST_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}_{BigInteger.Abs(new BigInteger(Guid.NewGuid().ToByteArray()))}";
            _masterConnectionString = new SqlConnectionStringBuilder(initialConnectionString) { InitialCatalog = "master" }.ToString();
            TestConnectionString = new SqlConnectionStringBuilder(initialConnectionString) { InitialCatalog = _databaseName }.ToString();

            var config = new SqlServerDataStoreConfiguration { ConnectionString = TestConnectionString, Initialize = true };

            var schemaUpgradeRunner = new SchemaUpgradeRunner(config, NullLogger<SchemaUpgradeRunner>.Instance);

            var schemaInformation = new SchemaInformation();

            _schemaInitializer = new SchemaInitializer(config, schemaUpgradeRunner, schemaInformation, NullLogger<SchemaInitializer>.Instance);

            var searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            searchParameterDefinitionManager.AllSearchParameters.Returns(new[]
            {
                new SearchParameter { Name = SearchParameterNames.Id, Type = SearchParamType.Token, Url = SearchParameterNames.IdUri.ToString() }.ToInfo(),
                new SearchParameter { Name = SearchParameterNames.LastUpdated, Type = SearchParamType.Date, Url = SearchParameterNames.LastUpdatedUri.ToString() }.ToInfo(),
            });

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

        public async Task InitializeAsync()
        {
            // Create the database
            using (var connection = new SqlConnection(_masterConnectionString))
            {
                await connection.OpenAsync();

                using (SqlCommand command = connection.CreateCommand())
                {
                    command.CommandTimeout = 600;
                    command.CommandText = $"CREATE DATABASE {_databaseName}";
                    await command.ExecuteNonQueryAsync();
                }
            }

            // verify that we can connect to the new database. This sometimes does not work right away with Azure SQL.
            await Policy
                .Handle<SqlException>()
                .WaitAndRetryAsync(
                    retryCount: 7,
                    sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)))
                .ExecuteAsync(async () =>
                {
                    using (var connection = new SqlConnection(TestConnectionString))
                    {
                        await connection.OpenAsync();
                        using (SqlCommand sqlCommand = connection.CreateCommand())
                        {
                            sqlCommand.CommandText = "SELECT 1";
                            await sqlCommand.ExecuteScalarAsync();
                        }
                    }
                });

            _schemaInitializer.Start();
        }

        public async Task DisposeAsync()
        {
            using (var connection = new SqlConnection(_masterConnectionString))
            {
                await connection.OpenAsync();
                SqlConnection.ClearAllPools();

                using (SqlCommand command = connection.CreateCommand())
                {
                    command.CommandTimeout = 600;
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
