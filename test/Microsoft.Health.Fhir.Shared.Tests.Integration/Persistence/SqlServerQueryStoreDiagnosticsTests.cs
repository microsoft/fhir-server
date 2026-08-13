// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    /// <summary>
    /// Exercises the SQL Server Query Store diagnostic procedures against captured FHIR database activity.
    /// </summary>
    [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class SqlServerQueryStoreDiagnosticsTests : IClassFixture<FhirStorageTestsFixture>
    {
        private const int QueryExecutionCount = 4;
        private const int QueryStorePollAttempts = 15;
        private static readonly TimeSpan QueryStorePollInterval = TimeSpan.FromSeconds(1);
        private readonly FhirStorageTestsFixture _fixture;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqlServerQueryStoreDiagnosticsTests"/> class.
        /// </summary>
        /// <param name="fixture">The SQL Server-backed FHIR storage fixture.</param>
        public SqlServerQueryStoreDiagnosticsTests(FhirStorageTestsFixture fixture)
        {
            _fixture = fixture;
        }

        /// <summary>
        /// Verifies the slow-query, plan diagnostics, and statistics health contracts with Query Store runtime data.
        /// </summary>
        /// <returns>A task that represents the asynchronous test operation.</returns>
        [Fact]
        public async Task GivenAQueryStoreCapturedFhirQuery_WhenDiagnosticsProceduresAreCalled_ThenReturnSanitizedPlanAndStatisticsMetadata()
        {
            using SqlConnection connection = await _fixture.SqlHelper.GetSqlConnectionAsync();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(CancellationToken.None);
            }

            await EnableAndVerifyQueryStoreAsync(connection, CancellationToken.None);

            string queryMarker = $"QueryStoreDiagnostics{Guid.NewGuid():N}";
            DateTimeOffset windowStart = DateTimeOffset.UtcNow.AddMinutes(-5);
            for (int execution = 0; execution < QueryExecutionCount; execution++)
            {
                using SqlCommand command = connection.CreateCommand();
                command.CommandText = $"SELECT COUNT_BIG(*) AS [{queryMarker}] FROM dbo.Resource;";
                object result = await command.ExecuteScalarAsync(CancellationToken.None);
                Assert.NotNull(result);
            }

            await WaitForQueryStoreCaptureAsync(connection, queryMarker, CancellationToken.None);

            long planId = await GetSlowQueryPlanIdAsync(
                connection,
                queryMarker,
                windowStart,
                DateTimeOffset.UtcNow.AddMinutes(1),
                CancellationToken.None);

            await AssertPlanDiagnosticsAsync(connection, planId, queryMarker, CancellationToken.None);
            await AssertResourceStatisticsHealthAsync(connection, CancellationToken.None);
        }

        private static async Task EnableAndVerifyQueryStoreAsync(SqlConnection connection, CancellationToken cancellationToken)
        {
            string initialState = await GetQueryStoreStateAsync(connection, cancellationToken);

            if (!string.Equals(initialState, "READ_WRITE", StringComparison.OrdinalIgnoreCase))
            {
                await ExecuteNonQueryAsync(
                    connection,
                    "ALTER DATABASE CURRENT SET QUERY_STORE = ON;",
                    cancellationToken);
            }

            await ExecuteNonQueryAsync(
                connection,
                "ALTER DATABASE CURRENT SET QUERY_STORE (OPERATION_MODE = READ_WRITE, QUERY_CAPTURE_MODE = ALL);",
                cancellationToken);

            string finalState = await GetQueryStoreStateAsync(connection, cancellationToken);
            Assert.True(
                string.Equals(finalState, "READ_WRITE", StringComparison.OrdinalIgnoreCase),
                $"Query Store is not writable after enablement (state: {finalState ?? "unknown"}).");
        }

        private static async Task<string> GetQueryStoreStateAsync(SqlConnection connection, CancellationToken cancellationToken)
        {
            using SqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT actual_state_desc FROM sys.database_query_store_options;";
            return (string)await command.ExecuteScalarAsync(cancellationToken);
        }

        private static async Task WaitForQueryStoreCaptureAsync(SqlConnection connection, string queryMarker, CancellationToken cancellationToken)
        {
            for (int attempt = 0; attempt < QueryStorePollAttempts; attempt++)
            {
                await ExecuteNonQueryAsync(connection, "EXEC sys.sp_query_store_flush_db;", cancellationToken);

                using SqlCommand command = connection.CreateCommand();
                command.CommandText = """
                SELECT COUNT_BIG(*)
                FROM sys.query_store_query_text
                WHERE query_sql_text LIKE @queryTextPattern;
                """;
                command.Parameters.Add("@queryTextPattern", SqlDbType.NVarChar, 256).Value = $"%{queryMarker}%";

                long capturedQueryCount = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
                if (capturedQueryCount > 0)
                {
                    return;
                }

                await Task.Delay(QueryStorePollInterval, cancellationToken);
            }

            Assert.Fail("Query Store did not persist the diagnostic query after the supported flush and polling window.");
        }

        private static async Task<long> GetSlowQueryPlanIdAsync(
            SqlConnection connection,
            string queryMarker,
            DateTimeOffset windowStart,
            DateTimeOffset windowEnd,
            CancellationToken cancellationToken)
        {
            using SqlCommand command = connection.CreateCommand();
            command.CommandType = CommandType.StoredProcedure;
            command.CommandText = "dbo.GetQueryStoreSlowQueries";
            command.Parameters.Add("@StartTime", SqlDbType.DateTimeOffset).Value = windowStart;
            command.Parameters.Add("@EndTime", SqlDbType.DateTimeOffset).Value = windowEnd;
            command.Parameters.Add("@Top", SqlDbType.Int).Value = 10;
            command.Parameters.Add("@Offset", SqlDbType.Int).Value = 0;
            command.Parameters.Add("@OrderBy", SqlDbType.VarChar, 32).Value = "Executions";
            command.Parameters.Add("@MinExecutions", SqlDbType.BigInt).Value = QueryExecutionCount;
            command.Parameters.Add("@QueryTextContains", SqlDbType.NVarChar, 256).Value = queryMarker;

            using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            Assert.True(await reader.ReadAsync(cancellationToken), "The uniquely marked Query Store query was not returned by dbo.GetQueryStoreSlowQueries.");

            long planId = reader.GetInt64(reader.GetOrdinal("PlanId"));
            Assert.True(planId > 0);
            Assert.True(reader.GetInt64(reader.GetOrdinal("QueryId")) > 0);
            Assert.True(reader.GetInt64(reader.GetOrdinal("RegularExecutionCount")) >= QueryExecutionCount);
            Assert.Contains(queryMarker, reader.GetString(reader.GetOrdinal("QuerySqlText")));
            Assert.False(reader.IsDBNull(reader.GetOrdinal("FirstExecutionTimeUtc")));
            Assert.False(reader.IsDBNull(reader.GetOrdinal("LastExecutionTimeUtc")));
            Assert.False(await reader.ReadAsync(cancellationToken), "The unique query-text filter returned more than one Query Store plan.");
            Assert.False(await reader.NextResultAsync(cancellationToken), "dbo.GetQueryStoreSlowQueries returned more than one result set.");

            return planId;
        }

        private static async Task AssertPlanDiagnosticsAsync(
            SqlConnection connection,
            long planId,
            string queryMarker,
            CancellationToken cancellationToken)
        {
            using SqlCommand command = connection.CreateCommand();
            command.CommandType = CommandType.StoredProcedure;
            command.CommandText = "dbo.GetQueryStorePlanDiagnostics";
            command.Parameters.Add("@PlanId", SqlDbType.BigInt).Value = planId;

            using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            Assert.True(await reader.ReadAsync(cancellationToken), "dbo.GetQueryStorePlanDiagnostics did not return the selected plan.");
            Assert.Equal(planId, reader.GetInt64(reader.GetOrdinal("PlanId")));
            Assert.True(reader.GetInt64(reader.GetOrdinal("QueryId")) > 0);
            Assert.Contains(queryMarker, reader.GetString(reader.GetOrdinal("QuerySqlText")));
            Assert.False(reader.IsDBNull(reader.GetOrdinal("CompatibilityLevel")));

            string sanitizationStatus = reader.GetString(reader.GetOrdinal("SanitizationStatus"));
            int sanitizedShowPlanXmlOrdinal = reader.GetOrdinal("SanitizedShowPlanXml");

            if (string.Equals(sanitizationStatus, "Sanitized", StringComparison.Ordinal))
            {
                Assert.False(reader.IsDBNull(sanitizedShowPlanXmlOrdinal));

                string sanitizedShowPlanXml = reader.GetValue(sanitizedShowPlanXmlOrdinal).ToString()!;
                Assert.False(sanitizedShowPlanXml.Contains("ParameterList", StringComparison.OrdinalIgnoreCase));
                Assert.False(sanitizedShowPlanXml.Contains("ParameterCompiledValue", StringComparison.OrdinalIgnoreCase));
                Assert.False(sanitizedShowPlanXml.Contains("ParameterRuntimeValue", StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                Assert.Contains(sanitizationStatus, new[] { "PlanXmlUnavailable", "InvalidXml", "VerificationFailed" });
                Assert.True(reader.IsDBNull(sanitizedShowPlanXmlOrdinal));
                Assert.False(reader.IsDBNull(reader.GetOrdinal("SanitizationErrorCode")));
            }

            Assert.False(await reader.ReadAsync(cancellationToken), "dbo.GetQueryStorePlanDiagnostics returned more than one row.");
            Assert.False(await reader.NextResultAsync(cancellationToken), "dbo.GetQueryStorePlanDiagnostics returned more than one result set.");
        }

        private static async Task AssertResourceStatisticsHealthAsync(SqlConnection connection, CancellationToken cancellationToken)
        {
            using SqlCommand command = connection.CreateCommand();
            command.CommandType = CommandType.StoredProcedure;
            command.CommandText = "dbo.GetStatisticsHealth";
            command.Parameters.Add("@TableName", SqlDbType.NVarChar, 128).Value = "Resource";
            command.Parameters.Add("@Top", SqlDbType.Int).Value = 1;
            command.Parameters.Add("@Offset", SqlDbType.Int).Value = 0;
            command.Parameters.Add("@OrderBy", SqlDbType.VarChar, 32).Value = "Rows";

            using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            Assert.True(await reader.ReadAsync(cancellationToken), "dbo.GetStatisticsHealth did not return metadata for dbo.Resource.");
            Assert.Equal("Resource", reader.GetString(reader.GetOrdinal("TableName")));
            Assert.False(reader.IsDBNull(reader.GetOrdinal("StatisticsName")));
            Assert.True(reader.GetInt32(reader.GetOrdinal("StatisticsId")) > 0);
            Assert.False(reader.IsDBNull(reader.GetOrdinal("StatisticsColumns")));
            Assert.Contains("StatisticsColumn", reader.GetValue(reader.GetOrdinal("StatisticsColumns")).ToString());
            Assert.Contains(reader.GetString(reader.GetOrdinal("StatisticsStatus")), new[] { "Available", "PropertiesUnavailable" });
            Assert.False(await reader.ReadAsync(cancellationToken), "The bounded Resource statistics request returned more than one row.");
            Assert.False(await reader.NextResultAsync(cancellationToken), "dbo.GetStatisticsHealth returned more than one result set.");
        }

        private static async Task ExecuteNonQueryAsync(SqlConnection connection, string commandText, CancellationToken cancellationToken)
        {
            using SqlCommand command = connection.CreateCommand();
            command.CommandText = commandText;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
