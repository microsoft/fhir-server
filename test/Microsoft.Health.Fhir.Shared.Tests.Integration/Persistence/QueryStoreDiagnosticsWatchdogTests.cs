// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
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

        // The probe table's row count at the point its statistics are updated, and the number of rows inserted
        // afterwards. They are deliberately different from each other and from any other value asserted below, so a
        // reordering of the positionally read statistics columns cannot go unnoticed.
        private const int ProbeTableRowCount = 200;
        private const int ProbeTableModificationCount = 5;

        // Wall-clock timing on the client is coarser than Query Store's own measurement, and the first execution pays
        // for compilation, so the upper bound gets a fixed allowance on top of the measured elapsed time.
        private const double ProbeTimingToleranceMilliseconds = 5000;

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

            // Query Store does not preserve comments in query_sql_text, so the probe cannot be tagged with a marker
            // comment. A GUID-derived result-column alias is preserved and makes the probe query self-identifying.
            string queryAlias = $"probe_{Guid.NewGuid():N}";

            await using SqlConnection connection = await _fixture.SqlConnectionBuilder.GetSqlConnectionAsync(cancellationToken: CancellationToken.None);
            await connection.OpenAsync(CancellationToken.None);

            try
            {
                await EnableAndVerifyQueryStoreAsync(connection, CancellationToken.None);
                await CreateProbeTableAsync(connection, tableName, CancellationToken.None);
                double probeElapsedMilliseconds = await ExecuteProbeQueryAsync(connection, tableName, queryAlias, CancellationToken.None);
                await WaitForQueryStoreCaptureAsync(connection, queryAlias, CancellationToken.None);
                await PrepareStatisticsProbeAsync(connection, tableName, CancellationToken.None);

                // Act
                await watchdog.RunWorkForTestingAsync(CancellationToken.None);

                // Assert
                // The probe query is grouped by plan_id, and a recompile between executions would produce a second
                // plan and therefore a second notification. That is a legitimate outcome, so the assertions are on
                // the whole matching set: what must hold is that the executions add up.
                List<SlowQueryNotification> probeNotifications = notifications
                    .OfType<SlowQueryNotification>()
                    .Where(notification => notification.QueryText.Contains(queryAlias, StringComparison.Ordinal))
                    .ToList();
                Assert.NotEmpty(probeNotifications);

                // The probe runs a fixed number of times under a GUID alias, so the rollup across Query Store
                // intervals and plans must sum to exactly that count.
                Assert.Equal((long)QueryExecutionCount, probeNotifications.Sum(notification => notification.ExecutionCount));

                // Query Store records microseconds and the contract is milliseconds. The probe runs at MAXDOP 1 and
                // is timed on the client, so the reported totals must sit inside the wall clock plus a tolerance. A
                // missing /1000.0 would inflate these by three orders of magnitude and break the upper bound; a
                // doubly applied one would sink them below MinDurationMilliseconds and the query would never appear.
                double durationUpperBoundMilliseconds = probeElapsedMilliseconds + ProbeTimingToleranceMilliseconds;
                foreach (SlowQueryNotification slowQuery in probeNotifications)
                {
                    Assert.True(slowQuery.QueryId > 0);
                    Assert.True(slowQuery.PlanId > 0);
                    Assert.True(slowQuery.QueryTextLength > 0);
                    Assert.True(slowQuery.ExecutionCount > 0);

                    Assert.InRange(slowQuery.TotalDurationMilliseconds, 1, durationUpperBoundMilliseconds);
                    Assert.InRange(slowQuery.AverageDurationMilliseconds, 1, durationUpperBoundMilliseconds);
                    Assert.InRange(slowQuery.MaxDurationMilliseconds, 1, durationUpperBoundMilliseconds);

                    // The probe is a three-way cross join at MAXDOP 1, so its CPU is provably non-trivial: a lower
                    // bound of zero would let a regression that zeroed CPU entirely pass.
                    Assert.InRange(slowQuery.TotalCpuMilliseconds, 1, durationUpperBoundMilliseconds);
                    Assert.InRange(slowQuery.AverageCpuMilliseconds, 1, durationUpperBoundMilliseconds);

                    // Query Store stores per-interval averages, so the emitted average must be the count-weighted
                    // one rather than an unweighted mean across intervals.
                    Assert.Equal(slowQuery.TotalDurationMilliseconds / slowQuery.ExecutionCount, slowQuery.AverageDurationMilliseconds, 3);
                    Assert.Equal(slowQuery.TotalCpuMilliseconds / slowQuery.ExecutionCount, slowQuery.AverageCpuMilliseconds, 3);
                    Assert.True(slowQuery.TotalLogicalReads > 0);

                    // Wait collection is best-effort and its failure is swallowed so that runtime metrics still
                    // publish. A status other than Failed is therefore the only proof that the wait SQL executed.
                    Assert.Contains(
                        slowQuery.WaitStatisticsStatus,
                        new[] { QueryStoreDiagnosticsWatchdog.WaitStatisticsAvailableStatus, QueryStoreDiagnosticsWatchdog.WaitStatisticsUnavailableStatus });
                    if (string.Equals(slowQuery.WaitStatisticsStatus, QueryStoreDiagnosticsWatchdog.WaitStatisticsAvailableStatus, StringComparison.Ordinal))
                    {
                        Assert.NotNull(slowQuery.TotalWaitMilliseconds);
                        Assert.NotNull(slowQuery.AverageWaitMilliseconds);
                        Assert.False(string.IsNullOrEmpty(slowQuery.TopWaitCategory));
                    }
                    else
                    {
                        Assert.Null(slowQuery.TotalWaitMilliseconds);
                    }

                    QueryPlanNotification queryPlan = Assert.Single(
                        notifications.OfType<QueryPlanNotification>().ToList(),
                        notification => notification.QueryId == slowQuery.QueryId && notification.PlanId == slowQuery.PlanId);
                    Assert.Equal(QueryPlanSanitizer.SanitizedStatus, queryPlan.SanitizationStatus);
                    Assert.NotNull(queryPlan.SanitizedQueryPlan);
                }

                AssertStatisticsHealthOrdinals(notifications, tableName);

                foreach (SlowQueryNotification slowQuery in notifications.OfType<SlowQueryNotification>())
                {
                    Assert.DoesNotContain("query_store", slowQuery.QueryText, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("dm_db_stats_properties", slowQuery.QueryText, StringComparison.OrdinalIgnoreCase);
                }
            }
            finally
            {
                await DropProbeTableAsync(connection, tableName, CancellationToken.None);
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
        public async Task GivenTheWatchdogIsStarted_WhenItRuns_ThenItNeitherSeedsNorReadsAnyDboParametersRow()
        {
            // Arrange
            // The feature is configured exclusively through configuration, so starting it must leave dbo.Parameters
            // untouched. Only a live database can show that: the seeding insert and the period read this watchdog no
            // longer performs both happened at startup, before the first tick, and neither is visible to a test that
            // invokes the collection directly.
            var mediator = Substitute.For<IMediator>();

            // A one-second period keeps the randomized start-up delay inside the test's own budget. The lease's first
            // acquire attempt is a full lease period away, so no tick of this watchdog reaches a collection here —
            // which is the point: what is under test is what running it costs the database before it collects
            // anything.
            var watchdog = CreateWatchdog(mediator, enabled: true, periodSec: 1);

            await using SqlConnection connection = await _fixture.SqlConnectionBuilder.GetSqlConnectionAsync(cancellationToken: CancellationToken.None);
            await connection.OpenAsync(CancellationToken.None);

            // A database this test has run the pre-refactor code against still holds the rows it seeded, and they
            // would make the assertion below pass for the wrong reason.
            await DeleteWatchdogParametersAsync(connection, CancellationToken.None);

            using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            // Act
            try
            {
                await watchdog.ExecuteAsync(cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected whenever the token trips while a randomized start-up delay is still pending, which is the
                // usual case. Cancelling between ticks instead returns normally, so neither outcome is asserted on.
            }

            // Assert
            // Had the watchdog seeded anything, the rows would be here. Had it read a period or an enablement flag
            // from a row it did not seed, it would have thrown InvalidOperationException out of ExecuteAsync rather
            // than being cancelled, because no such row exists.
            Assert.Equal(0, await CountWatchdogParametersAsync(connection, CancellationToken.None));
        }

        private static void AssertStatisticsHealthOrdinals(List<IMetricsNotification> notifications, string probeTableName)
        {
            // Every column of the statistics-health read is taken positionally from a hand-written SELECT list that
            // no compiler checks, so two same-typed columns could be reordered and every value would silently swap.
            // The probe table is set up so that its statistics carry values that differ from one another, which pins
            // those ordinals: a swap would have to preserve every one of these values to go unnoticed.
            List<StatisticsHealthNotification> statisticsHealth = notifications.OfType<StatisticsHealthNotification>().ToList();
            Assert.NotEmpty(statisticsHealth);

            StatisticsHealthNotification probeIndexStatistics = Assert.Single(
                statisticsHealth,
                notification =>
                    string.Equals(notification.SchemaName, "dbo", StringComparison.Ordinal)
                    && string.Equals(notification.TableName, probeTableName, StringComparison.Ordinal)
                    && string.Equals(notification.StatisticsName, $"PK_{probeTableName}", StringComparison.Ordinal));

            // The probe table holds ProbeTableRowCount rows at the point its statistics were updated with a full
            // scan, and exactly ProbeTableModificationCount rows were added afterwards, so every numeric column has
            // a known and distinct value rather than a coincidentally equal one.
            Assert.NotNull(probeIndexStatistics.LastUpdated);
            Assert.NotNull(probeIndexStatistics.Rows);
            Assert.NotNull(probeIndexStatistics.RowsSampled);
            Assert.NotNull(probeIndexStatistics.ModificationCounter);
            Assert.Equal((long)ProbeTableRowCount, probeIndexStatistics.Rows.Value);
            Assert.Equal((long)ProbeTableRowCount, probeIndexStatistics.RowsSampled.Value);
            Assert.Equal((long)ProbeTableModificationCount, probeIndexStatistics.ModificationCounter.Value);
            Assert.NotNull(probeIndexStatistics.ModificationPercent);
            Assert.Equal(ProbeTableModificationCount * 100.0 / ProbeTableRowCount, probeIndexStatistics.ModificationPercent.Value, 6);

            // Statistics backed by an index report is_from_index, and nothing else.
            Assert.True(probeIndexStatistics.IsFromIndex);
            Assert.False(probeIndexStatistics.IsAutoCreated);
            Assert.False(probeIndexStatistics.IsUserCreated);
            Assert.False(probeIndexStatistics.HasFilter);

            // A standalone CREATE STATISTICS object reports user_created, and nothing else, which separates that
            // flag from the three bit columns adjacent to it.
            StatisticsHealthNotification probeUserStatistics = Assert.Single(
                statisticsHealth,
                notification =>
                    string.Equals(notification.SchemaName, "dbo", StringComparison.Ordinal)
                    && string.Equals(notification.TableName, probeTableName, StringComparison.Ordinal)
                    && string.Equals(notification.StatisticsName, $"ST_{probeTableName}", StringComparison.Ordinal));
            Assert.True(probeUserStatistics.IsUserCreated);
            Assert.False(probeUserStatistics.IsFromIndex);
            Assert.False(probeUserStatistics.IsAutoCreated);
            Assert.False(probeUserStatistics.HasFilter);

            // A real FHIR table is asserted on as well, so that the scan is not merely finding the table this test
            // created. This index is filtered, which is what separates has_filter from is_from_index.
            StatisticsHealthNotification filteredIndexStatistics = Assert.Single(
                statisticsHealth,
                notification =>
                    string.Equals(notification.SchemaName, "dbo", StringComparison.Ordinal)
                    && string.Equals(notification.TableName, "Resource", StringComparison.Ordinal)
                    && string.Equals(notification.StatisticsName, "IX_Resource_ResourceTypeId_ResourceId", StringComparison.Ordinal));
            Assert.True(filteredIndexStatistics.HasFilter);
            Assert.True(filteredIndexStatistics.IsFromIndex);
            Assert.False(filteredIndexStatistics.IsAutoCreated);
            Assert.False(filteredIndexStatistics.IsUserCreated);
        }

        private QueryStoreDiagnosticsWatchdog CreateWatchdog(IMediator mediator, bool enabled, double periodSec = 300)
        {
            var configuration = new WatchdogConfiguration();
            configuration.QueryStoreDiagnostics.Enabled = enabled;
            configuration.QueryStoreDiagnostics.PeriodSec = periodSec;
            configuration.QueryStoreDiagnostics.SlowQueryCount = 100;
            configuration.QueryStoreDiagnostics.MinDurationMilliseconds = 1;
            configuration.QueryStoreDiagnostics.IncludeQueryPlans = true;
            configuration.QueryStoreDiagnostics.IncludeStatisticsHealth = true;

            // High enough to cover every statistics object in the FHIR schema, so that the assertions on named
            // statistics do not depend on where the staleness ordering happens to place them.
            configuration.QueryStoreDiagnostics.StatisticsHealthCount = 5000;

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
                "ALTER DATABASE CURRENT SET QUERY_STORE (OPERATION_MODE = READ_WRITE, QUERY_CAPTURE_MODE = ALL, WAIT_STATS_CAPTURE_MODE = ON);",
                cancellationToken);

            Assert.Equal("READ_WRITE", await GetQueryStoreStateAsync(connection, cancellationToken), ignoreCase: true);
        }

        private static async Task<string> GetQueryStoreStateAsync(SqlConnection connection, CancellationToken cancellationToken)
        {
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT actual_state_desc FROM sys.database_query_store_options;";
            return (string)await command.ExecuteScalarAsync(cancellationToken);
        }

        private static async Task DeleteWatchdogParametersAsync(SqlConnection connection, CancellationToken cancellationToken)
        {
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = "DELETE FROM dbo.Parameters WHERE Id LIKE 'QueryStoreDiagnosticsWatchdog%';";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static async Task<int> CountWatchdogParametersAsync(SqlConnection connection, CancellationToken cancellationToken)
        {
            // Matched by prefix rather than by the two names the watchdog used to seed, so a row this feature has no
            // business creating is caught whatever it is called.
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM dbo.Parameters WHERE Id LIKE 'QueryStoreDiagnosticsWatchdog%';";
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        }

        private static async Task CreateProbeTableAsync(SqlConnection connection, string tableName, CancellationToken cancellationToken)
        {
            // The primary key is named explicitly so that the statistics object it backs has a predictable name to
            // assert on; an unnamed constraint would get a generated one.
            await ExecuteNonQueryAsync(
                connection,
                $"CREATE TABLE dbo.[{tableName}] (Id int NOT NULL CONSTRAINT [PK_{tableName}] PRIMARY KEY); INSERT INTO dbo.[{tableName}] (Id) SELECT TOP ({ProbeTableRowCount}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) FROM sys.all_objects;",
                cancellationToken);
        }

        private static async Task PrepareStatisticsProbeAsync(SqlConnection connection, string tableName, CancellationToken cancellationToken)
        {
            // A full-scan update fixes rows and rows_sampled at the current row count and sets last_updated; the
            // rows inserted afterwards then fix modification_counter at a different, known value. Without this the
            // table's statistics have never been updated and dm_db_stats_properties reports nulls throughout, which
            // asserts nothing about which column was read.
            string commandText = $@"
UPDATE STATISTICS dbo.[{tableName}] WITH FULLSCAN;
CREATE STATISTICS [ST_{tableName}] ON dbo.[{tableName}] (Id) WITH FULLSCAN;
INSERT INTO dbo.[{tableName}] (Id) SELECT TOP ({ProbeTableModificationCount}) {ProbeTableRowCount} + ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) FROM sys.all_objects;";

            await ExecuteNonQueryAsync(connection, commandText, cancellationToken);
        }

        private static async Task DropProbeTableAsync(SqlConnection connection, string tableName, CancellationToken cancellationToken)
        {
            await ExecuteNonQueryAsync(connection, $"DROP TABLE IF EXISTS dbo.[{tableName}];", cancellationToken);
        }

        private static async Task<double> ExecuteProbeQueryAsync(SqlConnection connection, string tableName, string queryAlias, CancellationToken cancellationToken)
        {
            // MAXDOP 1 keeps CPU time comparable with elapsed time, so the millisecond assertions have a meaningful
            // upper bound rather than one inflated by an unknown degree of parallelism.
            var stopwatch = Stopwatch.StartNew();
            for (int execution = 0; execution < QueryExecutionCount; execution++)
            {
                await using SqlCommand command = connection.CreateCommand();
                command.CommandText = $"SELECT SUM(CONVERT(bigint, firstProbe.Id) * secondProbe.Id * thirdProbe.Id) AS [{queryAlias}] FROM dbo.[{tableName}] AS firstProbe CROSS JOIN dbo.[{tableName}] AS secondProbe CROSS JOIN dbo.[{tableName}] AS thirdProbe OPTION (MAXDOP 1);";
                object result = await command.ExecuteScalarAsync(cancellationToken);
                Assert.NotNull(result);
            }

            stopwatch.Stop();
            return stopwatch.Elapsed.TotalMilliseconds;
        }

        private static async Task WaitForQueryStoreCaptureAsync(SqlConnection connection, string queryAlias, CancellationToken cancellationToken)
        {
            for (int attempt = 0; attempt < QueryStorePollAttempts; attempt++)
            {
                await ExecuteNonQueryAsync(connection, "EXEC sys.sp_query_store_flush_db;", cancellationToken);

                await using SqlCommand command = connection.CreateCommand();

                // Wait for every execution to be persisted, not merely the first, so the execution-count assertion
                // cannot race the flush.
                command.CommandText = @"
SELECT ISNULL(SUM(runtimeStats.count_executions), 0)
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

                long capturedExecutionCount = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
                if (capturedExecutionCount >= QueryExecutionCount)
                {
                    return;
                }

                await Task.Delay(QueryStorePollInterval, cancellationToken);
            }

            Assert.Fail("Query Store did not persist regular runtime statistics for every execution of the GUID-alias probe query after the supported flush and polling window.");
        }

        private static async Task ExecuteNonQueryAsync(SqlConnection connection, string commandText, CancellationToken cancellationToken)
        {
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = commandText;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
