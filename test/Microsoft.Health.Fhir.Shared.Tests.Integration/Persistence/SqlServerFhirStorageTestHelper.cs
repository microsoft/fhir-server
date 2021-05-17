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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Configs;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Schema.Manager;
using NSubstitute;
using Polly;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    public class SqlServerFhirStorageTestHelper : IFhirStorageTestHelper, ISqlServerFhirStorageTestHelper
    {
        private readonly string _masterDatabaseName;
        private readonly string _initialConnectionString;
        private readonly SqlServerFhirModel _sqlServerFhirModel;
        private readonly ISqlConnectionFactory _sqlConnectionFactory;

        public SqlServerFhirStorageTestHelper(
            string initialConnectionString,
            string masterDatabaseName,
            SqlServerFhirModel sqlServerFhirModel,
            ISqlConnectionFactory sqlConnectionFactory)
        {
            EnsureArg.IsNotNull(sqlServerFhirModel, nameof(sqlServerFhirModel));
            EnsureArg.IsNotNull(sqlConnectionFactory, nameof(sqlConnectionFactory));

            _masterDatabaseName = masterDatabaseName;
            _initialConnectionString = initialConnectionString;
            _sqlServerFhirModel = sqlServerFhirModel;
            _sqlConnectionFactory = sqlConnectionFactory;
        }

        public async Task CreateAndInitializeDatabase(string databaseName, int maximumSupportedSchemaVersion, bool forceIncrementalSchemaUpgrade, SchemaInitializer schemaInitializer = null, CancellationToken cancellationToken = default)
        {
            var testConnectionString = new SqlConnectionStringBuilder(_initialConnectionString) { InitialCatalog = databaseName }.ToString();
            schemaInitializer = schemaInitializer ?? CreateSchemaInitializer(testConnectionString, maximumSupportedSchemaVersion);

            // Create the database.
            using (var connection = await _sqlConnectionFactory.GetSqlConnectionAsync(_masterDatabaseName, cancellationToken))
            {
                await connection.OpenAsync(cancellationToken);

                using (SqlCommand command = connection.CreateCommand())
                {
                    command.CommandTimeout = 600;
                    command.CommandText = @$"
                        IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = '{databaseName}')
                        BEGIN
                          CREATE DATABASE {databaseName};
                        END";
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            // Verify that we can connect to the new database. This sometimes does not work right away with Azure SQL.
            await Policy
                .Handle<SqlException>()
                .WaitAndRetryAsync(
                    retryCount: 7,
                    sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)))
                .ExecuteAsync(async () =>
                {
                    using (var connection = await _sqlConnectionFactory.GetSqlConnectionAsync(databaseName, cancellationToken))
                    {
                        await connection.OpenAsync(cancellationToken);
                        using (SqlCommand sqlCommand = connection.CreateCommand())
                        {
                            sqlCommand.CommandText = "SELECT 1";
                            await sqlCommand.ExecuteScalarAsync(cancellationToken);
                        }
                    }
                });

            await schemaInitializer.InitializeAsync(forceIncrementalSchemaUpgrade, cancellationToken);
            await _sqlServerFhirModel.Initialize(maximumSupportedSchemaVersion, true, cancellationToken);
        }

        public async Task DeleteDatabase(string databaseName, CancellationToken cancellationToken = default)
        {
            using (var connection = await _sqlConnectionFactory.GetSqlConnectionAsync(_masterDatabaseName, cancellationToken))
            {
                await connection.OpenAsync(cancellationToken);
                SqlConnection.ClearAllPools();

                using (SqlCommand command = connection.CreateCommand())
                {
                    command.CommandTimeout = 600;
                    command.CommandText = $"DROP DATABASE IF EXISTS {databaseName}";
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }
            }
        }

        public async Task DeleteAllExportJobRecordsAsync(CancellationToken cancellationToken = default)
        {
            using (var connection = await _sqlConnectionFactory.GetSqlConnectionAsync())
            {
                var command = new SqlCommand("DELETE FROM dbo.ExportJob", connection);

                await command.Connection.OpenAsync(cancellationToken);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        public async Task DeleteExportJobRecordAsync(string id, CancellationToken cancellationToken = default)
        {
            using (var connection = await _sqlConnectionFactory.GetSqlConnectionAsync())
            {
                var command = new SqlCommand("DELETE FROM dbo.ExportJob WHERE Id = @id", connection);

                var parameter = new SqlParameter { ParameterName = "@id", Value = id };
                command.Parameters.Add(parameter);

                await command.Connection.OpenAsync(cancellationToken);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        public async Task DeleteSearchParameterStatusAsync(string uri, CancellationToken cancellationToken = default)
        {
            using (var connection = await _sqlConnectionFactory.GetSqlConnectionAsync())
            {
                var command = new SqlCommand("DELETE FROM dbo.SearchParam WHERE Uri = @uri", connection);
                command.Parameters.AddWithValue("@uri", uri);

                await command.Connection.OpenAsync(cancellationToken);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            _sqlServerFhirModel.RemoveSearchParamIdToUriMapping(uri);
        }

        public async Task DeleteAllReindexJobRecordsAsync(CancellationToken cancellationToken = default)
        {
            using (var connection = await _sqlConnectionFactory.GetSqlConnectionAsync())
            {
                var command = new SqlCommand("DELETE FROM dbo.ReindexJob", connection);

                await command.Connection.OpenAsync(cancellationToken);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        public async Task DeleteReindexJobRecordAsync(string id, CancellationToken cancellationToken = default)
        {
            using (var connection = await _sqlConnectionFactory.GetSqlConnectionAsync())
            {
                var command = new SqlCommand("DELETE FROM dbo.ReindexJob WHERE Id = @id", connection);

                var parameter = new SqlParameter { ParameterName = "@id", Value = id };
                command.Parameters.Add(parameter);

                await command.Connection.OpenAsync(cancellationToken);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        async Task<object> IFhirStorageTestHelper.GetSnapshotToken()
        {
            using (var connection = await _sqlConnectionFactory.GetSqlConnectionAsync())
            {
                await connection.OpenAsync();

                SqlCommand command = connection.CreateCommand();
                command.CommandText = "SELECT MAX(ResourceSurrogateId) FROM dbo.Resource";
                return await command.ExecuteScalarAsync();
            }
        }

        async Task IFhirStorageTestHelper.ValidateSnapshotTokenIsCurrent(object snapshotToken)
        {
            using (var connection = await _sqlConnectionFactory.GetSqlConnectionAsync())
            {
                await connection.OpenAsync();

                var sb = new StringBuilder();
                using (SqlCommand outerCommand = connection.CreateCommand())
                {
                    outerCommand.CommandText = @"
                    SELECT t.name 
                    FROM sys.tables t
                    INNER JOIN sys.columns c ON c.object_id = t.object_id
                    WHERE c.name = 'ResourceSurrogateId'";

                    using (SqlDataReader reader = await outerCommand.ExecuteReaderAsync())
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

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = sb.ToString();
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            Assert.True(reader.IsDBNull(1) || reader.GetInt64(1) <= (long)snapshotToken);
                        }
                    }
                }
            }
        }

        private SchemaInitializer CreateSchemaInitializer(string testConnectionString, int maxSupportedSchemaVersion)
        {
            var schemaOptions = new SqlServerSchemaOptions { AutomaticUpdatesEnabled = true };
            var config = new SqlServerDataStoreConfiguration { ConnectionString = testConnectionString, Initialize = true, SchemaOptions = schemaOptions };
            var schemaInformation = new SchemaInformation(SchemaVersionConstants.Min, maxSupportedSchemaVersion);
            var scriptProvider = new ScriptProvider<SchemaVersion>();
            var baseScriptProvider = new BaseScriptProvider();
            var mediator = Substitute.For<IMediator>();
            var sqlConnectionStringProvider = new DefaultSqlConnectionStringProvider(config);
            var sqlConnectionFactory = new DefaultSqlConnectionFactory(sqlConnectionStringProvider);
            var schemaManagerDataStore = new SchemaManagerDataStore(sqlConnectionFactory);
            var schemaUpgradeRunner = new SchemaUpgradeRunner(scriptProvider, baseScriptProvider, NullLogger<SchemaUpgradeRunner>.Instance, sqlConnectionFactory, schemaManagerDataStore);

            return new SchemaInitializer(config, schemaUpgradeRunner, schemaInformation, sqlConnectionFactory, sqlConnectionStringProvider, mediator, NullLogger<SchemaInitializer>.Instance);
        }
    }
}
