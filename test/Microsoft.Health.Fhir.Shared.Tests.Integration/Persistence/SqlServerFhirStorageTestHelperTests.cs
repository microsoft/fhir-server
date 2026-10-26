// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    /// <summary>
    /// Verifies database creation for local SQL Server and Azure SQL elastic pools.
    /// </summary>
    [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class SqlServerFhirStorageTestHelperTests
    {
        /// <summary>
        /// Verifies that unconfigured pools preserve standalone database creation.
        /// </summary>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        public void GivenNoElasticPool_WhenBuildingCreateDatabaseStatement_UsesStandaloneDatabase(string elasticPoolName)
        {
            const string databaseName = "test-database";

            var statement = SqlServerFhirStorageTestHelper.BuildCreateDatabaseStatement(databaseName, elasticPoolName);

            Assert.Equal("CREATE DATABASE [test-database];", statement);
        }

        /// <summary>
        /// Verifies that configured pools are selected with quoted SQL identifiers.
        /// </summary>
        [Theory]
        [InlineData("test-database", "pr-pool", "CREATE DATABASE [test-database] (SERVICE_OBJECTIVE = ELASTIC_POOL (NAME = [pr-pool]));")]
        [InlineData("test]database", "pr]pool", "CREATE DATABASE [test]]database] (SERVICE_OBJECTIVE = ELASTIC_POOL (NAME = [pr]]pool]));")]
        public void GivenAnElasticPool_WhenBuildingCreateDatabaseStatement_SelectsPool(string databaseName, string elasticPoolName, string expectedStatement)
        {
            var statement = SqlServerFhirStorageTestHelper.BuildCreateDatabaseStatement(databaseName, elasticPoolName);

            Assert.Equal(expectedStatement, statement);
        }

        /// <summary>
        /// Verifies that fixture-created databases belong to the configured Azure SQL elastic pool.
        /// </summary>
        [Fact]
        public async Task GivenAnElasticPool_WhenInitializingFixture_DatabaseBelongsToPool()
        {
            var elasticPoolName = EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.SqlServerElasticPoolName);
            Assert.SkipWhen(string.IsNullOrWhiteSpace(elasticPoolName), "Requires an Azure SQL elastic pool.");
            var fixture = new SqlServerFhirStorageTestsFixture();

            await fixture.InitializeAsync();

            try
            {
                await using var connection = await fixture.SqlConnectionBuilder.GetSqlConnectionAsync("master", cancellationToken: CancellationToken.None);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT elastic_pool_name FROM sys.database_service_objectives WHERE database_id = DB_ID(@databaseName)";
                command.Parameters.AddWithValue("@databaseName", new SqlConnectionStringBuilder(fixture.TestConnectionString).InitialCatalog);

                Assert.Equal(elasticPoolName, (string)await command.ExecuteScalarAsync());
            }
            finally
            {
                await fixture.DisposeAsync();
            }
        }
    }
}
