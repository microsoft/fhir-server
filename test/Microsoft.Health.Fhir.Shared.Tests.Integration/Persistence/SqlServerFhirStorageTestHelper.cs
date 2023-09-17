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
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs;
using Microsoft.Health.JobManagement.UnitTests;
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
        private readonly TestQueueClient _queueClient;

        public SqlServerFhirStorageTestHelper(
            string initialConnectionString,
            string masterDatabaseName,
            SqlServerFhirModel sqlServerFhirModel,
            ISqlConnectionBuilder sqlConnectionBuilder,
            TestQueueClient queueClient)
        {
            EnsureArg.IsNotNull(sqlServerFhirModel, nameof(sqlServerFhirModel));
            EnsureArg.IsNotNull(sqlConnectionBuilder, nameof(sqlConnectionBuilder));

            _masterDatabaseName = masterDatabaseName;
            _initialConnectionString = initialConnectionString;
            _sqlServerFhirModel = sqlServerFhirModel;
            _sqlConnectionBuilder = sqlConnectionBuilder;
            _queueClient = queueClient;

            _dbSetupRetryPolicy = Policy
                .Handle<Exception>()
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
                await using SqlConnection connection = await _sqlConnectionBuilder.GetSqlConnectionAsync(_masterDatabaseName, null, cancellationToken);
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
                await using SqlConnection connection = await _sqlConnectionBuilder.GetSqlConnectionAsync(databaseName, null, cancellationToken);
                await connection.OpenAsync(cancellationToken);
                await using SqlCommand sqlCommand = connection.CreateCommand();
                sqlCommand.CommandText = "SELECT 1";
                await sqlCommand.ExecuteScalarAsync(cancellationToken);
                await connection.CloseAsync();
            });

            await _dbSetupRetryPolicy.ExecuteAsync(async () =>
            {
                await schemaInitializer.InitializeAsync(forceIncrementalSchemaUpgrade, cancellationToken);
            });
            await InitWatchdogsParameters();
            await _sqlServerFhirModel.Initialize(maximumSupportedSchemaVersion, true, cancellationToken);
        }

        public async Task InitWatchdogsParameters()
        {
            await using var conn = await _sqlConnectionBuilder.GetSqlConnectionAsync(cancellationToken: CancellationToken.None);
            await conn.OpenAsync(CancellationToken.None);
            using var cmd = new SqlCommand(
                @"
INSERT INTO dbo.Parameters (Id,Number) SELECT @IsEnabledId, 1
INSERT INTO dbo.Parameters (Id,Number) SELECT @ThreadsId, 4
INSERT INTO dbo.Parameters (Id,Number) SELECT @PeriodSecId, 5
INSERT INTO dbo.Parameters (Id,Number) SELECT @LeasePeriodSecId, 2
INSERT INTO dbo.Parameters (Id,Number) SELECT @HeartbeatPeriodSecId, 2
INSERT INTO dbo.Parameters (Id,Number) SELECT @HeartbeatTimeoutSecId, 10
INSERT INTO dbo.Parameters (Id,Number) SELECT 'Defrag.MinFragPct', 0
INSERT INTO dbo.Parameters (Id,Number) SELECT 'Defrag.MinSizeGB', 0.01
                ",
                conn);
            var defragWatchdog = new DefragWatchdog();
            cmd.Parameters.AddWithValue("@IsEnabledId", defragWatchdog.IsEnabledId);
            cmd.Parameters.AddWithValue("@ThreadsId", defragWatchdog.ThreadsId);
            cmd.Parameters.AddWithValue("@PeriodSecId", defragWatchdog.PeriodSecId);
            cmd.Parameters.AddWithValue("@LeasePeriodSecId", defragWatchdog.LeasePeriodSecId);
            cmd.Parameters.AddWithValue("@HeartbeatPeriodSecId", defragWatchdog.HeartbeatPeriodSecId);
            cmd.Parameters.AddWithValue("@HeartbeatTimeoutSecId", defragWatchdog.HeartbeatTimeoutSecId);
            await cmd.ExecuteNonQueryAsync(CancellationToken.None);

            using var cmd2 = new SqlCommand(
                @"
INSERT INTO dbo.Parameters (Id,Number) SELECT @PeriodSecId, 5
INSERT INTO dbo.Parameters (Id,Number) SELECT @LeasePeriodSecId, 2
INSERT INTO dbo.Parameters (Id,Number) SELECT 'CleanupEventLog.DeleteBatchSize', 1000
INSERT INTO dbo.Parameters (Id,Number) SELECT 'CleanupEventLog.AllowedRows', 1000
INSERT INTO dbo.Parameters (Id,Number) SELECT 'CleanupEventLog.RetentionPeriodDay', 1.0/24/3600
INSERT INTO dbo.Parameters (Id,Number) SELECT 'CleanupEventLog.IsEnabled', 1
                ",
                conn);
            var cleanupWatchdog = new CleanupEventLogWatchdog();
            cmd2.Parameters.AddWithValue("@PeriodSecId", cleanupWatchdog.PeriodSecId);
            cmd2.Parameters.AddWithValue("@LeasePeriodSecId", cleanupWatchdog.LeasePeriodSecId);
            await cmd2.ExecuteNonQueryAsync(CancellationToken.None);

            using var cmd3 = new SqlCommand(
                @"
INSERT INTO dbo.Parameters (Id,Number) SELECT @PeriodSecId, 2
INSERT INTO dbo.Parameters (Id,Number) SELECT @LeasePeriodSecId, 5
                ",
                conn);
            var transactionWatchdog = new TransactionWatchdog();
            cmd3.Parameters.AddWithValue("@PeriodSecId", transactionWatchdog.PeriodSecId);
            cmd3.Parameters.AddWithValue("@LeasePeriodSecId", transactionWatchdog.LeasePeriodSecId);
            await cmd3.ExecuteNonQueryAsync(CancellationToken.None);

            using var cmd4 = new SqlCommand(
                @"
INSERT INTO dbo.Parameters (Id,Number) SELECT @PeriodSecId, 5
INSERT INTO dbo.Parameters (Id,Number) SELECT @LeasePeriodSecId, 10
                ",
                conn);
            var invisibleHistoryCleanupWatchdog = new InvisibleHistoryCleanupWatchdog();
            cmd4.Parameters.AddWithValue("@PeriodSecId", invisibleHistoryCleanupWatchdog.PeriodSecId);
            cmd4.Parameters.AddWithValue("@LeasePeriodSecId", invisibleHistoryCleanupWatchdog.LeasePeriodSecId);
            await cmd4.ExecuteNonQueryAsync(CancellationToken.None);

            await conn.CloseAsync();
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
            SqlConnection.ClearAllPools();

            await _dbSetupRetryPolicy.ExecuteAsync(async () =>
            {
                await using SqlConnection connection = await _sqlConnectionBuilder.GetSqlConnectionAsync(_masterDatabaseName, null, cancellationToken);
                await connection.OpenAsync(cancellationToken);
                await using SqlCommand command = connection.CreateCommand();
                command.CommandTimeout = 600;
                command.CommandText = $"DROP DATABASE IF EXISTS {databaseName}";
                await command.ExecuteNonQueryAsync(cancellationToken);
                await connection.CloseAsync();
            });
        }

        public Task DeleteAllExportJobRecordsAsync(CancellationToken cancellationToken = default)
        {
            _queueClient.JobInfos.Clear();
            return Task.CompletedTask;
        }

        public Task DeleteExportJobRecordAsync(string id, CancellationToken cancellationToken = default)
        {
            _queueClient.JobInfos.RemoveAll((info) => info.Id == long.Parse(id));
            return Task.CompletedTask;
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
  FROM (SELECT name, object_id FROM sys.objects WHERE name NOT IN ('ResourceCurrent', 'ResourceHistory') AND type IN ('u','v')) t 
       JOIN sys.columns c ON c.object_id = t.object_id
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
            sqlConnection.GetSqlConnectionAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>()).ReturnsForAnyArgs((x) => Task.FromResult(GetSqlConnection(testConnectionString)));
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

        public async Task<SqlConnection> GetSqlConnectionAsync()
        {
            return await _sqlConnectionBuilder.GetSqlConnectionAsync(cancellationToken: CancellationToken.None);
        }

        protected SqlConnection GetSqlConnection(string connectionString)
        {
            var connectionBuilder = new SqlConnectionStringBuilder(connectionString);
            var result = new SqlConnection(connectionBuilder.ToString());
            return result;
        }
    }
}
