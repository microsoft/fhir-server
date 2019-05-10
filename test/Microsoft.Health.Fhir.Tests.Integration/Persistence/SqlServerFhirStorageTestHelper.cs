// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data.SqlClient;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    public class SqlServerFhirStorageTestHelper : IFhirStorageTestHelper
    {
        private readonly string _connectionString;

        public SqlServerFhirStorageTestHelper(string connectionString)
        {
            _connectionString = connectionString;
        }

        public Task DeleteAllExportJobRecordsAsync()
        {
            throw new System.NotImplementedException();
        }

        async Task<object> IFhirStorageTestHelper.GetSnapshotToken()
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                SqlCommand command = connection.CreateCommand();
                command.CommandText = "SELECT MAX(ResourceSurrogateId) FROM dbo.Resource";
                return await command.ExecuteScalarAsync();
            }
        }

        async Task IFhirStorageTestHelper.ValidateSnapshotTokenIsCurrent(object snapshotToken)
        {
            using (var connection = new SqlConnection(_connectionString))
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
    }
}
