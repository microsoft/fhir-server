// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Configs;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Schema.Manager;
using Microsoft.Health.SqlServer.Features.Storage;
using NSubstitute;
using Polly;
using Polly.Retry;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    public class SqlServerFhirStorageTestHelper : IFhirStorageTestHelper, ISqlServerFhirStorageTestHelper
    {
        private readonly string _masterDatabaseName;
        private readonly string _initialConnectionString;
        private readonly SqlServerFhirModel _sqlServerFhirModel;
        private readonly ISqlConnectionBuilder _sqlConnectionBuilder;
        private readonly AsyncRetryPolicy _dbSetupRetryPolicy;

        public SqlServerFhirStorageTestHelper(
            string initialConnectionString,
            string masterDatabaseName,
            SqlServerFhirModel sqlServerFhirModel,
            ISqlConnectionBuilder sqlConnectionBuilder)
        {
            EnsureArg.IsNotNull(sqlServerFhirModel, nameof(sqlServerFhirModel));
            EnsureArg.IsNotNull(sqlConnectionBuilder, nameof(sqlConnectionBuilder));

            _masterDatabaseName = masterDatabaseName;
            _initialConnectionString = initialConnectionString;
            _sqlServerFhirModel = sqlServerFhirModel;
            _sqlConnectionBuilder = sqlConnectionBuilder;

            _dbSetupRetryPolicy = Policy
                .Handle<SqlException>()
                .WaitAndRetryAsync(
                    retryCount: 7,
                    sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
        }

        public async Task CreateAndInitializeDatabase(string databaseName, int maximumSupportedSchemaVersion, bool forceIncrementalSchemaUpgrade, SchemaInitializer schemaInitializer = null, CancellationToken cancellationToken = default)
        {
            var testConnectionString = new SqlConnectionStringBuilder(_initialConnectionString) { InitialCatalog = databaseName }.ToString();
            schemaInitializer ??= CreateSchemaInitializer(testConnectionString, maximumSupportedSchemaVersion);

            await _dbSetupRetryPolicy.ExecuteAsync(async () =>
            {
                // Create the database.
                await using SqlConnection connection = await _sqlConnectionBuilder.GetSqlConnectionAsync(_masterDatabaseName, cancellationToken);
                await connection.OpenAsync(cancellationToken);

                await using SqlCommand command = connection.CreateCommand();
                command.CommandTimeout = 600;
                command.CommandText = @$"
                        IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = '{databaseName}')
                        BEGIN
                          CREATE DATABASE {databaseName};
                        END";
                await command.ExecuteNonQueryAsync(cancellationToken);
                await connection.CloseAsync();
            });

            // Verify that we can connect to the new database. This sometimes does not work right away with Azure SQL.

            await _dbSetupRetryPolicy.ExecuteAsync(async () =>
            {
                await using SqlConnection connection = await _sqlConnectionBuilder.GetSqlConnectionAsync(databaseName, cancellationToken);
                await connection.OpenAsync(cancellationToken);
                await using SqlCommand sqlCommand = connection.CreateCommand();
                sqlCommand.CommandText = "SELECT 1";
                await sqlCommand.ExecuteScalarAsync(cancellationToken);
                await connection.CloseAsync();
            });

            await schemaInitializer.InitializeAsync(forceIncrementalSchemaUpgrade, cancellationToken);
            await _sqlServerFhirModel.Initialize(maximumSupportedSchemaVersion, true, cancellationToken);
        }

        public async Task ExecuteSqlCmd(string sql)
        {
            await using SqlConnection connection = await _sqlConnectionBuilder.GetSqlConnectionAsync(cancellationToken: CancellationToken.None);
            using SqlCommand command = new SqlCommand(sql, connection);
            await connection.OpenAsync(CancellationToken.None);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
            await connection.CloseAsync();
        }

        public async Task DeleteDatabase(string databaseName, CancellationToken cancellationToken = default)
        {
            await _dbSetupRetryPolicy.ExecuteAsync(async () =>
            {
                await using SqlConnection connection = await _sqlConnectionBuilder.GetSqlConnectionAsync(_masterDatabaseName, cancellationToken);
                await connection.OpenAsync(cancellationToken);
                await using SqlCommand command = connection.CreateCommand();
                command.CommandTimeout = 600;
                command.CommandText = $"ALTER DATABASE {databaseName} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;";
                await command.ExecuteNonQueryAsync(cancellationToken);
                command.CommandText = $"DROP DATABASE IF EXISTS {databaseName}";
                await command.ExecuteNonQueryAsync(cancellationToken);
                await connection.CloseAsync();
            });
        }

        public async Task DeleteAllExportJobRecordsAsync(CancellationToken cancellationToken = default)
        {
            await using SqlConnection connection = await _sqlConnectionBuilder.GetSqlConnectionAsync(cancellationToken: cancellationToken);
            var command = new SqlCommand("DELETE FROM dbo.JobQueue WHERE QueueType = @QueueType", connection);
            command.Parameters.AddWithValue("@QueueType", Core.Features.Operations.QueueType.Export);
            await command.Connection.OpenAsync(cancellationToken);
            await command.ExecuteNonQueryAsync(cancellationToken);
            await connection.CloseAsync();
        }

        public async Task DeleteExportJobRecordAsync(string id, CancellationToken cancellationToken = default)
        {
            await using SqlConnection connection = await _sqlConnectionBuilder.GetSqlConnectionAsync(cancellationToken: cancellationToken);
            var command = new SqlCommand("DELETE FROM dbo.JobQueue WHERE QueueType = @QueueType AND JobId = @id", connection);
            command.Parameters.AddWithValue("@QueueType", Core.Features.Operations.QueueType.Export);
            command.Parameters.AddWithValue("@id", id);
            await command.Connection.OpenAsync(cancellationToken);
            await command.ExecuteNonQueryAsync(cancellationToken);
            await connection.CloseAsync();
        }

        public async Task DeleteSearchParameterStatusAsync(string uri, CancellationToken cancellationToken = default)
        {
            await using SqlConnection connection = await _sqlConnectionBuilder.GetSqlConnectionAsync(cancellationToken: cancellationToken);
            var command = new SqlCommand("DELETE FROM dbo.SearchParam WHERE Uri = @uri", connection);
            command.Parameters.AddWithValue("@uri", uri);

            await command.Connection.OpenAsync(cancellationToken);
            await command.ExecuteNonQueryAsync(cancellationToken);
            await connection.CloseAsync();
            _sqlServerFhirModel.RemoveSearchParamIdToUriMapping(uri);
        }

        public async Task DeleteAllReindexJobRecordsAsync(CancellationToken cancellationToken = default)
        {
            await using SqlConnection connection = await _sqlConnectionBuilder.GetSqlConnectionAsync(cancellationToken: cancellationToken);
            var command = new SqlCommand("DELETE FROM dbo.ReindexJob", connection);

            await command.Connection.OpenAsync(cancellationToken);
            await command.ExecuteNonQueryAsync(cancellationToken);
            await connection.CloseAsync();
        }

        public async Task DeleteReindexJobRecordAsync(string id, CancellationToken cancellationToken = default)
        {
            await using SqlConnection connection = await _sqlConnectionBuilder.GetSqlConnectionAsync(cancellationToken: cancellationToken);
            var command = new SqlCommand("DELETE FROM dbo.ReindexJob WHERE Id = @id", connection);

            var parameter = new SqlParameter { ParameterName = "@id", Value = id };
            command.Parameters.Add(parameter);

            await command.Connection.OpenAsync(cancellationToken);
            await command.ExecuteNonQueryAsync(cancellationToken);
            await connection.CloseAsync();
        }

        async Task<object> IFhirStorageTestHelper.GetSnapshotToken()
        {
            await using SqlConnection connection = await _sqlConnectionBuilder.GetSqlConnectionAsync();
            await connection.OpenAsync();

            SqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT MAX(ResourceSurrogateId) FROM dbo.Resource";
            return await command.ExecuteScalarAsync();
        }

        async Task IFhirStorageTestHelper.ValidateSnapshotTokenIsCurrent(object snapshotToken)
        {
            await using SqlConnection connection = await _sqlConnectionBuilder.GetSqlConnectionAsync();
            await connection.OpenAsync();

            var sb = new StringBuilder();
            await using (SqlCommand outerCommand = connection.CreateCommand())
            {
                outerCommand.CommandText = @"
                    SELECT t.name 
                    FROM sys.tables t
                    INNER JOIN sys.columns c ON c.object_id = t.object_id
                    WHERE c.name = 'ResourceSurrogateId'";

                await using (SqlDataReader reader = await outerCommand.ExecuteReaderAsync())
                {
                    while (reader.Read())
                    {
                        if (sb.Length > 0)
                        {
                            sb.AppendLine("UNION ALL");
                        }

                        string tableName = reader.GetString(0);
                        sb.AppendLine($"SELECT '{tableName}' as TableName, MAX(ResourceSurrogateId) as MaxResourceSurrogateId FROM dbo.{tableName}");
                    }
                }
            }

            await using (SqlCommand command = connection.CreateCommand())
            {
                command.CommandText = sb.ToString();
                await using (SqlDataReader reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        Assert.True(reader.IsDBNull(1) || reader.GetInt64(1) <= (long)snapshotToken);
                    }
                }
            }

            await connection.CloseAsync();
        }

        private SchemaInitializer CreateSchemaInitializer(string testConnectionString, int maxSupportedSchemaVersion)
        {
            var schemaOptions = new SqlServerSchemaOptions { AutomaticUpdatesEnabled = true };
            var config = Options.Create(new SqlServerDataStoreConfiguration { ConnectionString = testConnectionString, Initialize = true, SchemaOptions = schemaOptions, StatementTimeout = TimeSpan.FromMinutes(10) });
            var schemaInformation = new SchemaInformation(SchemaVersionConstants.Min, maxSupportedSchemaVersion);

            var sqlConnection = Substitute.For<ISqlConnectionBuilder>();
            sqlConnection.GetSqlConnectionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ReturnsForAnyArgs((x) => Task.FromResult(GetSqlConnection(testConnectionString)));
            SqlRetryLogicBaseProvider sqlRetryLogicBaseProvider = SqlConfigurableRetryFactory.CreateFixedRetryProvider(new SqlClientRetryOptions().Settings);

            var sqlServerDataStoreConfiguration = new SqlServerDataStoreConfiguration() { ConnectionString = testConnectionString };
            ISqlConnectionStringProvider sqlConnectionString = new DefaultSqlConnectionStringProvider(Options.Create(sqlServerDataStoreConfiguration));
            var sqlConnectionWrapperFactory = new SqlConnectionWrapperFactory(new SqlTransactionHandler(), sqlConnection, sqlRetryLogicBaseProvider, config);
            var schemaManagerDataStore = new SchemaManagerDataStore(sqlConnectionWrapperFactory, config, NullLogger<SchemaManagerDataStore>.Instance);
            var schemaUpgradeRunner = new SchemaUpgradeRunner(new ScriptProvider<SchemaVersion>(), new BaseScriptProvider(), NullLogger<SchemaUpgradeRunner>.Instance, sqlConnectionWrapperFactory, schemaManagerDataStore);

            Func<IServiceProvider, ISqlConnectionStringProvider> sqlConnectionStringProvider = p => sqlConnectionString;
            Func<IServiceProvider, SqlConnectionWrapperFactory> sqlConnectionWrapperFactoryFunc = p => sqlConnectionWrapperFactory;
            Func<IServiceProvider, SchemaUpgradeRunner> schemaUpgradeRunnerFactory = p => schemaUpgradeRunner;
            Func<IServiceProvider, IReadOnlySchemaManagerDataStore> schemaManagerDataStoreFactory = p => schemaManagerDataStore;

            var collection = new ServiceCollection();
            collection.AddScoped(sqlConnectionStringProvider);
            collection.AddScoped(sqlConnectionWrapperFactoryFunc);
            collection.AddScoped(schemaManagerDataStoreFactory);
            collection.AddScoped(schemaUpgradeRunnerFactory);
            var serviceProvider = collection.BuildServiceProvider();
            return new SchemaInitializer(serviceProvider, config, schemaInformation, Substitute.For<IMediator>(), NullLogger<SchemaInitializer>.Instance);
        }

        protected SqlConnection GetSqlConnection(string connectionString)
        {
            var connectionBuilder = new SqlConnectionStringBuilder(connectionString);
            var result = new SqlConnection(connectionBuilder.ToString());
            return result;
        }
    }
}
