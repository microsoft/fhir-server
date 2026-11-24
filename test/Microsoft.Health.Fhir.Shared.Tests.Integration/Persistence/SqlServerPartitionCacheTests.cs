// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.SqlServer.Features.Partitions;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Configs;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    /// <summary>
    /// Loads SQL partition caches from real partition databases through one application connection string.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Caching)]
    public class SqlServerPartitionCacheTests : IAsyncLifetime
    {
        private const string ParameterId = "Test.Partition.Value";

        private readonly string _serverConnectionString = EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.SqlServerConnectionString);
        private readonly string _applicationDatabase = NewDatabaseName("app");
        private readonly string _alphaDatabase = NewDatabaseName("alpha");
        private readonly string _betaDatabase = NewDatabaseName("beta");
        private ISqlConnectionBuilder _connectionBuilder;

        public async ValueTask InitializeAsync()
        {
            await CreateDatabaseAsync(_applicationDatabase, "CustomQuery_app", parameterValue: null);
            await CreateDatabaseAsync(_alphaDatabase, "CustomQuery_alpha", parameterValue: 1);
            await CreateDatabaseAsync(_betaDatabase, "CustomQuery_beta", parameterValue: 2);

            _connectionBuilder = new DefaultSqlConnectionBuilder(
                Options.Create(new SqlServerDataStoreConfiguration { ConnectionString = GetConnectionString(_applicationDatabase) }),
                SqlConfigurableRetryFactory.CreateNoneRetryProvider());
        }

        public async ValueTask DisposeAsync()
        {
            SqlConnection.ClearAllPools();
            foreach (string database in new[] { _applicationDatabase, _alphaDatabase, _betaDatabase })
            {
                await ExecuteAsync(null, $"IF DB_ID('{database}') IS NOT NULL BEGIN ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}] END");
            }
        }

        [Fact]
        public async Task GivenProceduresInEachPartitionDatabase_WhenLoaded_ThenEachPartitionSeesOnlyItsOwnProcedures()
        {
            var cache = new CustomQueryCache(_connectionBuilder, NullLogger<CustomQueryCache>.Instance);

            CustomQueryProcedures alpha = await cache.GetAsync(Partition("alpha", _alphaDatabase));
            CustomQueryProcedures beta = cache.Get(Partition("beta", _betaDatabase));
            CustomQueryProcedures application = await cache.GetAsync(null);

            Assert.True(alpha.TryGetProcedureName("alpha", out _));
            Assert.False(alpha.TryGetProcedureName("beta", out _));
            Assert.False(alpha.TryGetProcedureName("app", out _));

            Assert.True(beta.TryGetProcedureName("beta", out _));
            Assert.False(beta.TryGetProcedureName("alpha", out _));

            Assert.True(application.TryGetProcedureName("app", out _));
            Assert.False(application.TryGetProcedureName("alpha", out _));
        }

        [Fact]
        public async Task GivenParametersInEachPartitionDatabase_WhenLoaded_ThenEachPartitionSeesOnlyItsOwnValues()
        {
            var cache = new DatabaseParameterCache(_connectionBuilder, NullLogger<DatabaseParameterCache>.Instance);

            DatabaseParameters alpha = await cache.GetAsync(Partition("alpha", _alphaDatabase));
            DatabaseParameters beta = cache.Get(Partition("beta", _betaDatabase));
            DatabaseParameters application = await cache.GetAsync(null);

            Assert.Equal(1, alpha.GetNumber(ParameterId, defaultValue: -1));
            Assert.Equal(2, beta.GetNumber(ParameterId, defaultValue: -1));

            // The application database has no dbo.Parameters table, so every parameter takes its default.
            Assert.Equal(-1, application.GetNumber(ParameterId, defaultValue: -1));
        }

        private static SqlPartitionInfo Partition(string id, string database) => new() { Id = id, Database = database };

        private static string NewDatabaseName(string partition) => $"PartitionCache_{partition}_{Guid.NewGuid():N}";

        private string GetConnectionString(string database) =>
            new SqlConnectionStringBuilder(_serverConnectionString) { InitialCatalog = database ?? "master" }.ToString();

        private async Task CreateDatabaseAsync(string database, string procedureName, int? parameterValue)
        {
            await ExecuteAsync(null, $"CREATE DATABASE [{database}]");
            await ExecuteAsync(database, $"CREATE PROCEDURE dbo.[{procedureName}] AS SELECT 1");
            if (parameterValue.HasValue)
            {
                await ExecuteAsync(database, $"CREATE TABLE dbo.Parameters (Id varchar(100) NOT NULL PRIMARY KEY, Number float NULL); INSERT INTO dbo.Parameters (Id, Number) VALUES ('{ParameterId}', {parameterValue.Value})");
            }
        }

        private async Task ExecuteAsync(string database, string sql)
        {
            await using var connection = new SqlConnection(GetConnectionString(database));
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
