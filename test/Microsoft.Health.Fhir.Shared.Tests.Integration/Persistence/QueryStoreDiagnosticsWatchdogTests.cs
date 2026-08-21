// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Medino;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Metrics;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class QueryStoreDiagnosticsWatchdogTests : IClassFixture<SqlServerFhirStorageTestsFixture>
    {
        private const int QueryExecutionCount = 2;
        private const int QueryStorePollAttempts = 15;
        private static readonly TimeSpan QueryStorePollInterval = TimeSpan.FromSeconds(1);
        private readonly SqlServerFhirStorageTestsFixture _fixture;

        public QueryStoreDiagnosticsWatchdogTests(SqlServerFhirStorageTestsFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task GivenEnabledWatchdogAndCapturedProbe_WhenRun_ThenPublishesSlowQuerySanitizedPlanAndStatisticsHealth()
        {
            // Arrange
            var mediator = Substitute.For<IMediator>();
            var notifications = new List<IMetricsNotification>();
            CaptureNotifications(mediator, notifications);
            var watchdog = CreateWatchdog(mediator, enabled: true);
            string tableName = $"DiagProbe_{Guid.NewGuid():N}";
            string queryAlias = $"probe_{Guid.NewGuid():N}";

            await using SqlConnection connection = await _fixture.SqlConnectionBuilder.GetSqlConnectionAsync(cancellationToken: CancellationToken.None);
            await connection.OpenAsync(CancellationToken.None);

            try
            {
                await EnableAndVerifyQueryStoreAsync(connection, CancellationToken.None);
                await SetWatchdogParametersAsync(connection, isEnabled: 1, periodSeconds: 300, CancellationToken.None);
                await CreateProbeTableAsync(connection, tableName, CancellationToken.None);
                await ExecuteProbeQueryAsync(connection, tableName, queryAlias, CancellationToken.None);
                await WaitForQueryStoreCaptureAsync(connection, queryAlias, CancellationToken.None);

                // Act
                await watchdog.RunWorkForTestingAsync(CancellationToken.None);

                // Assert
                SlowQueryNotification slowQuery = Assert.Single(
                    notifications.FindAll(notification => notification is SlowQueryNotification)
                        .ConvertAll(notification => (SlowQueryNotification)notification)
                        .FindAll(notification => notification.QueryText.Contains(queryAlias, StringComparison.Ordinal)));
                Assert.True(slowQuery.QueryId > 0);
                Assert.True(slowQuery.PlanId > 0);
                Assert.True(slowQuery.QueryTextLength > 0);

                QueryPlanNotification queryPlan = Assert.Single(
                    notifications.FindAll(notification => notification is QueryPlanNotification)
                        .ConvertAll(notification => (QueryPlanNotification)notification)
                        .FindAll(notification => notification.QueryId == slowQuery.QueryId && notification.PlanId == slowQuery.PlanId));
                Assert.Equal(QueryPlanSanitizer.SanitizedStatus, queryPlan.SanitizationStatus);
                Assert.NotNull(queryPlan.SanitizedQueryPlan);

                Assert.NotEmpty(notifications.FindAll(notification => notification is StatisticsHealthNotification));

                foreach (IMetricsNotification notification in notifications.FindAll(notification => notification is SlowQueryNotification))
                {
                    string queryText = ((SlowQueryNotification)notification).QueryText;
                    Assert.DoesNotContain("query_store", queryText, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("dm_db_stats_properties", queryText, StringComparison.OrdinalIgnoreCase);
                }
            }
            finally
            {
                await DropProbeTableAsync(connection, tableName, CancellationToken.None);
                await DeleteWatchdogParametersAsync(connection, CancellationToken.None);
            }
        }

        [Fact]
        public async Task GivenConfigurationGateDisabled_WhenRun_ThenPublishesNothing()
        {
            // Arrange
            var mediator = Substitute.For<IMediator>();
            var watchdog = CreateWatchdog(mediator, enabled: false);

            // Act
            await watchdog.RunWorkForTestingAsync(CancellationToken.None);

            // Assert
            Assert.Empty(mediator.ReceivedCalls());
        }

        [Fact]
        public async Task GivenRuntimeGateDisabled_WhenRun_ThenPublishesNothing()
        {
            // Arrange
            var mediator = Substitute.For<IMediator>();
            var watchdog = CreateWatchdog(mediator, enabled: true);
            await using SqlConnection connection = await _fixture.SqlConnectionBuilder.GetSqlConnectionAsync(cancellationToken: CancellationToken.None);
            await connection.OpenAsync(CancellationToken.None);
            await SetWatchdogParametersAsync(connection, isEnabled: 0, periodSeconds: 300, CancellationToken.None);

            try
            {
                // Act
                await watchdog.RunWorkForTestingAsync(CancellationToken.None);

                // Assert
                Assert.Empty(mediator.ReceivedCalls());
            }
            finally
            {
                await DeleteWatchdogParametersAsync(connection, CancellationToken.None);
            }
        }

        private QueryStoreDiagnosticsWatchdog CreateWatchdog(IMediator mediator, bool enabled)
        {
            var configuration = new WatchdogConfiguration();
            configuration.QueryStoreDiagnostics.Enabled = enabled;
            configuration.QueryStoreDiagnostics.PeriodSec = 300;
            configuration.QueryStoreDiagnostics.SlowQueryCount = 100;
            configuration.QueryStoreDiagnostics.MinDurationMilliseconds = 1;
            configuration.QueryStoreDiagnostics.IncludeQueryPlans = true;
            configuration.QueryStoreDiagnostics.IncludeStatisticsHealth = true;
            configuration.QueryStoreDiagnostics.StatisticsHealthCount = 50;

            return new QueryStoreDiagnosticsWatchdog(
                _fixture.SqlRetryService,
                NullLogger<QueryStoreDiagnosticsWatchdog>.Instance,
                mediator,
                Options.Create(configuration));
        }

        private static void CaptureNotifications(IMediator mediator, List<IMetricsNotification> notifications)
        {
            mediator.When(x => x.PublishAsync(Arg.Any<SlowQueryNotification>(), Arg.Any<CancellationToken>()))
                .Do(info => notifications.Add((SlowQueryNotification)info[0]));
            mediator.When(x => x.PublishAsync(Arg.Any<QueryPlanNotification>(), Arg.Any<CancellationToken>()))
                .Do(info => notifications.Add((QueryPlanNotification)info[0]));
            mediator.When(x => x.PublishAsync(Arg.Any<StatisticsHealthNotification>(), Arg.Any<CancellationToken>()))
                .Do(info => notifications.Add((StatisticsHealthNotification)info[0]));
        }

        private static async Task EnableAndVerifyQueryStoreAsync(SqlConnection connection, CancellationToken cancellationToken)
        {
            string initialState = await GetQueryStoreStateAsync(connection, cancellationToken);
            if (!string.Equals(initialState, "READ_WRITE", StringComparison.OrdinalIgnoreCase))
            {
                await ExecuteNonQueryAsync(connection, "ALTER DATABASE CURRENT SET QUERY_STORE = ON;", cancellationToken);
            }

            await ExecuteNonQueryAsync(
                connection,
                "ALTER DATABASE CURRENT SET QUERY_STORE (OPERATION_MODE = READ_WRITE, QUERY_CAPTURE_MODE = ALL);",
                cancellationToken);

            Assert.Equal("READ_WRITE", await GetQueryStoreStateAsync(connection, cancellationToken), ignoreCase: true);
        }

        private static async Task<string> GetQueryStoreStateAsync(SqlConnection connection, CancellationToken cancellationToken)
        {
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT actual_state_desc FROM sys.database_query_store_options;";
            return (string)await command.ExecuteScalarAsync(cancellationToken);
        }

        private static async Task SetWatchdogParametersAsync(SqlConnection connection, int isEnabled, int periodSeconds, CancellationToken cancellationToken)
        {
            await SetParameterAsync(connection, "QueryStoreDiagnosticsWatchdog.IsEnabled", isEnabled, cancellationToken);
            await SetParameterAsync(connection, "QueryStoreDiagnosticsWatchdog.PeriodSec", periodSeconds, cancellationToken);
        }

        private static async Task SetParameterAsync(SqlConnection connection, string id, double value, CancellationToken cancellationToken)
        {
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = @"
UPDATE dbo.Parameters SET Number = @Value WHERE Id = @Id;
IF @@ROWCOUNT = 0
BEGIN
    INSERT INTO dbo.Parameters (Id, Number) VALUES (@Id, @Value);
END";
            command.Parameters.AddWithValue("@Id", id);
            command.Parameters.AddWithValue("@Value", value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static async Task DeleteWatchdogParametersAsync(SqlConnection connection, CancellationToken cancellationToken)
        {
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = @"
DELETE FROM dbo.Parameters
WHERE Id IN ('QueryStoreDiagnosticsWatchdog.IsEnabled', 'QueryStoreDiagnosticsWatchdog.PeriodSec');";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static async Task CreateProbeTableAsync(SqlConnection connection, string tableName, CancellationToken cancellationToken)
        {
            await ExecuteNonQueryAsync(
                connection,
                $"CREATE TABLE dbo.[{tableName}] (Id int NOT NULL PRIMARY KEY); INSERT INTO dbo.[{tableName}] (Id) SELECT TOP (200) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) FROM sys.all_objects;",
                cancellationToken);
        }

        private static async Task DropProbeTableAsync(SqlConnection connection, string tableName, CancellationToken cancellationToken)
        {
            await ExecuteNonQueryAsync(connection, $"DROP TABLE IF EXISTS dbo.[{tableName}];", cancellationToken);
        }

        private static async Task ExecuteProbeQueryAsync(SqlConnection connection, string tableName, string queryAlias, CancellationToken cancellationToken)
        {
            for (int execution = 0; execution < QueryExecutionCount; execution++)
            {
                await using SqlCommand command = connection.CreateCommand();
                command.CommandText = $"SELECT SUM(CONVERT(bigint, firstProbe.Id) * secondProbe.Id * thirdProbe.Id) AS [{queryAlias}] FROM dbo.[{tableName}] AS firstProbe CROSS JOIN dbo.[{tableName}] AS secondProbe CROSS JOIN dbo.[{tableName}] AS thirdProbe;";
                object result = await command.ExecuteScalarAsync(cancellationToken);
                Assert.NotNull(result);
            }
        }

        private static async Task WaitForQueryStoreCaptureAsync(SqlConnection connection, string queryAlias, CancellationToken cancellationToken)
        {
            for (int attempt = 0; attempt < QueryStorePollAttempts; attempt++)
            {
                await ExecuteNonQueryAsync(connection, "EXEC sys.sp_query_store_flush_db;", cancellationToken);

                await using SqlCommand command = connection.CreateCommand();
                command.CommandText = @"
SELECT COUNT_BIG(*)
FROM sys.query_store_runtime_stats AS runtimeStats
INNER JOIN sys.query_store_plan AS queryPlan
    ON runtimeStats.plan_id = queryPlan.plan_id
INNER JOIN sys.query_store_query AS queryStoreQuery
    ON queryPlan.query_id = queryStoreQuery.query_id
INNER JOIN sys.query_store_query_text AS queryText
    ON queryStoreQuery.query_text_id = queryText.query_text_id
WHERE queryText.query_sql_text LIKE @QueryTextPattern
    AND runtimeStats.execution_type = 0;";
                command.Parameters.Add("@QueryTextPattern", SqlDbType.NVarChar, 256).Value = $"%{queryAlias}%";

                long capturedRuntimeStatisticsCount = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
                if (capturedRuntimeStatisticsCount > 0)
                {
                    return;
                }

                await Task.Delay(QueryStorePollInterval, cancellationToken);
            }

            Assert.Fail("Query Store did not persist regular runtime statistics for the GUID-alias probe query after the supported flush and polling window.");
        }

        private static async Task ExecuteNonQueryAsync(SqlConnection connection, string commandText, CancellationToken cancellationToken)
        {
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = commandText;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
