// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data.SqlClient;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
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
    public class SqlServerFhirStorageTestsFixture : IScoped<IFhirDataStore>, IFhirDataStoreStateVerifier
    {
        private readonly string _initialConnectionString;
        private readonly string _databaseName;

        public SqlServerFhirStorageTestsFixture()
        {
            _initialConnectionString = Environment.GetEnvironmentVariable("SqlServer:ConnectionString") ?? LocalDatabase.DefaultConnectionString;
            _databaseName = $"FHIRINTEGRATIONTEST_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}_{BigInteger.Abs(new BigInteger(Guid.NewGuid().ToByteArray()))}";

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

            var securityConfiguration = new SecurityConfiguration { LastModifiedClaims = { "oid" } };

            var sqlServerFhirModel = new SqlServerFhirModel(config, schemaInformation, searchParameterDefinitionManager, Options.Create(securityConfiguration), NullLogger<SqlServerFhirModel>.Instance);

            Value = new SqlServerFhirDataStore(config, sqlServerFhirModel, NullLogger<SqlServerFhirDataStore>.Instance);
        }

        public IFhirDataStore Value { get; }

        public string TestConnectionString { get; }

        public async Task<object> GetSnapshotToken()
        {
            using (var connection = new SqlConnection(TestConnectionString))
            {
                await connection.OpenAsync();

                SqlCommand command = connection.CreateCommand();
                command.CommandText = "SELECT MAX(ResourceSurrogateId) FROM Resource";
                return await command.ExecuteScalarAsync();
            }
        }

        public async Task ValidateSnapshotTokenIsCurrent(object snapshotToken)
        {
            using (var connection = new SqlConnection(TestConnectionString))
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
                        while (reader.Read())
                        {
                            Assert.True(reader.IsDBNull(1) || reader.GetInt64(1) <= (long)snapshotToken);
                        }
                    }
                }
            }
        }

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
    }
}
