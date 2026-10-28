// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs.QueryStoreDiagnostics;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs.QueryStoreDiagnostics.Models;
using Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Watchdogs.QueryStoreDiagnostics
{
    /// <summary>
    /// Covers the branch that reports a broken wait collection. The integration test can only assert that the status
    /// is not <c>Failed</c>, because it cannot make a live wait read fail, so without this the branch that exists
    /// specifically to make that breakage visible would never be executed by the suite.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class QueryStoreDiagnosticsWaitStatisticsTests
    {
        [Fact]
        public async Task GivenAStalledWaitRead_WhenCollecting_ThenRuntimeStatisticsAreEmitted()
        {
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            var logger = new CapturingLogger();
            var slowQuery = new QueryStoreDiagnosticsWatchdog.SlowQueryResult
            {
                QueryId = 11,
                PlanId = 22,
                ExecutionCount = 1,
                QueryText = "SELECT 1",
                IntervalStart = DateTimeOffset.UtcNow.AddMinutes(-5),
                IntervalEnd = DateTimeOffset.UtcNow,
            };

            ConfigureSlowQuery(sqlRetryService, slowQuery);
            ConfigureStalledWait(sqlRetryService);

            QueryStoreDiagnosticsWatchdog watchdog = CreateWatchdog(sqlRetryService, logger, waitStatisticsTimeoutSec: 1);
            using var cancellation = new CancellationTokenSource();

            try
            {
                await watchdog.CollectDiagnosticsAsync(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow, cancellation.Token)
                    .WaitAsync(TimeSpan.FromSeconds(7));
            }
            finally
            {
                cancellation.Cancel();
            }

            CapturingLogger.LogEntry entry = Assert.Single(logger.SlowQueryEntries);
            Assert.Equal(slowQuery.QueryId, entry.Properties["QueryId"]);
            Assert.Equal(QueryStoreDiagnosticsWatchdog.WaitStatisticsFailedStatus, entry.Properties["WaitStatisticsStatus"]);
        }

        [Fact]
        public async Task GivenAStalledWaitRead_WhenTheCollectionDeadlineExpires_ThenRuntimeStatisticsAreEmittedAndTheTickIsIncomplete()
        {
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            var logger = new CapturingLogger();
            var slowQuery = new QueryStoreDiagnosticsWatchdog.SlowQueryResult
            {
                QueryId = 11,
                PlanId = 22,
                ExecutionCount = 1,
                QueryText = "SELECT 1",
                IntervalStart = DateTimeOffset.UtcNow.AddMinutes(-5),
                IntervalEnd = DateTimeOffset.UtcNow,
            };
            int commandTimeout = 0;
            sqlRetryService
                .ExecuteReaderAsync(
                    Arg.Any<SqlCommand>(),
                    Arg.Any<Func<SqlDataReader, QueryStoreDiagnosticsWatchdog.QueryStoreState>>(),
                    Arg.Any<ILogger>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>(),
                    Arg.Any<bool>())
                .Returns(call =>
                {
                    commandTimeout = call.ArgAt<SqlCommand>(0).CommandTimeout;
                    return new List<QueryStoreDiagnosticsWatchdog.QueryStoreState> { new QueryStoreDiagnosticsWatchdog.QueryStoreState("READ_WRITE", null) };
                });
            ConfigureSlowQuery(sqlRetryService, slowQuery);
            ConfigureStalledWait(sqlRetryService);
            QueryStoreDiagnosticsWatchdog watchdog = CreateWatchdog(sqlRetryService, logger, collectionTimeoutSec: 1, waitStatisticsTimeoutSec: 5);

            await Assert.ThrowsAsync<TimeoutException>(() => watchdog.RunWorkForTestingAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(7)));

            CapturingLogger.LogEntry entry = Assert.Single(logger.SlowQueryEntries);
            Assert.Equal(slowQuery.QueryId, entry.Properties["QueryId"]);
            Assert.Equal(QueryStoreDiagnosticsWatchdog.WaitStatisticsFailedStatus, entry.Properties["WaitStatisticsStatus"]);
            Assert.Equal(5, commandTimeout);
            Assert.Contains(logger.WarningMessages, message => message.Contains("collection exceeded", StringComparison.Ordinal));
            Assert.DoesNotContain(logger.Messages, message => message.Contains("completed a collection", StringComparison.Ordinal));
        }

        [Fact]
        public async Task GivenRuntimeResultsArriveAfterTheDeadline_WhenCollecting_ThenNoWaitReadStarts()
        {
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            var logger = new CapturingLogger();
            var slowQuery = new QueryStoreDiagnosticsWatchdog.SlowQueryResult
            {
                QueryId = 11,
                PlanId = 22,
                ExecutionCount = 1,
                QueryText = "SELECT 1",
                IntervalStart = DateTimeOffset.UtcNow.AddMinutes(-5),
                IntervalEnd = DateTimeOffset.UtcNow,
            };
            sqlRetryService
                .ExecuteReaderAsync(
                    Arg.Any<SqlCommand>(),
                    Arg.Any<Func<SqlDataReader, QueryStoreDiagnosticsWatchdog.QueryStoreState>>(),
                    Arg.Any<ILogger>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>(),
                    Arg.Any<bool>())
                .Returns(new List<QueryStoreDiagnosticsWatchdog.QueryStoreState> { new QueryStoreDiagnosticsWatchdog.QueryStoreState("READ_WRITE", null) });
            sqlRetryService
                .ExecuteReaderAsync(
                    Arg.Any<SqlCommand>(),
                    Arg.Any<Func<SqlDataReader, QueryStoreDiagnosticsWatchdog.SlowQueryResult>>(),
                    Arg.Any<ILogger>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>(),
                    Arg.Any<bool>())
                .Returns(async _ =>
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(1500));
                    IReadOnlyList<QueryStoreDiagnosticsWatchdog.SlowQueryResult> results = new[] { slowQuery };
                    return results;
                });
            var waitStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ConfigureStalledWait(sqlRetryService, waitStarted);
            QueryStoreDiagnosticsWatchdog watchdog = CreateWatchdog(sqlRetryService, logger, collectionTimeoutSec: 1, waitStatisticsTimeoutSec: 5);

            await Assert.ThrowsAsync<TimeoutException>(() => watchdog.RunWorkForTestingAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(7)));

            Assert.False(waitStarted.Task.IsCompleted);
            CapturingLogger.LogEntry entry = Assert.Single(logger.SlowQueryEntries);
            Assert.Equal(slowQuery.QueryId, entry.Properties["QueryId"]);
            Assert.Equal(QueryStoreDiagnosticsWatchdog.WaitStatisticsFailedStatus, entry.Properties["WaitStatisticsStatus"]);
        }

        [Fact]
        public async Task GivenAStalledWaitRead_WhenTheHostCancels_ThenCancellationPropagatesWithoutEmittingRuntimeStatistics()
        {
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            var logger = new CapturingLogger();
            var slowQuery = new QueryStoreDiagnosticsWatchdog.SlowQueryResult
            {
                QueryId = 11,
                PlanId = 22,
                ExecutionCount = 1,
                QueryText = "SELECT 1",
            };
            var waitStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ConfigureSlowQuery(sqlRetryService, slowQuery);
            ConfigureStalledWait(sqlRetryService, waitStarted);
            QueryStoreDiagnosticsWatchdog watchdog = CreateWatchdog(sqlRetryService, logger);
            using var cancellation = new CancellationTokenSource();

            Task collection = watchdog.CollectDiagnosticsAsync(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow, cancellation.Token);
            await waitStarted.Task.WaitAsync(TimeSpan.FromSeconds(7));
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => collection);
            Assert.Empty(logger.SlowQueryEntries);
            Assert.Empty(logger.WarningMessages);
        }

        [Theory]
        [InlineData(1205, "wait statistics could not be read")]
        [InlineData(2812, "wait statistics stored procedure is missing")]
        public async Task GivenAFailingWaitStatisticsRead_WhenCollecting_ThenSlowQueriesAreLoggedWithFailedWaitStatus(int errorNumber, string expectedWarning)
        {
            // Arrange
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            var logger = new CapturingLogger();

            var slowQuery = new QueryStoreDiagnosticsWatchdog.SlowQueryResult
            {
                QueryId = 11,
                PlanId = 22,
                ExecutionCount = 4,
                TotalDurationMilliseconds = 400,
                AverageDurationMilliseconds = 100,
                MaxDurationMilliseconds = 150,
                TotalCpuMilliseconds = 200,
                AverageCpuMilliseconds = 50,
                TotalLogicalReads = 80,
                AverageLogicalReads = 20,
                QueryText = "SELECT 1",
                IntervalStart = DateTimeOffset.UtcNow.AddMinutes(-5),
                IntervalEnd = DateTimeOffset.UtcNow,
            };

            sqlRetryService
                .ExecuteReaderAsync(
                    Arg.Any<SqlCommand>(),
                    Arg.Any<Func<SqlDataReader, QueryStoreDiagnosticsWatchdog.SlowQueryResult>>(),
                    Arg.Any<ILogger>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>(),
                    Arg.Any<bool>())
                .Returns(new List<QueryStoreDiagnosticsWatchdog.SlowQueryResult> { slowQuery });

            sqlRetryService
                .ExecuteReaderAsync(
                    Arg.Any<SqlCommand>(),
                    Arg.Any<Func<SqlDataReader, QueryStoreDiagnosticsWatchdog.WaitStatistics>>(),
                    Arg.Any<ILogger>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>(),
                    Arg.Any<bool>())
                .Returns<IReadOnlyList<QueryStoreDiagnosticsWatchdog.WaitStatistics>>(
                    _ => throw SqlExceptionFactory.GetSqlException(errorNumber, "Wait statistics read failed."));

            QueryStoreDiagnosticsWatchdog watchdog = CreateWatchdog(sqlRetryService, logger);

            // Act
            await watchdog.CollectDiagnosticsAsync(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow, CancellationToken.None);

            // Assert
            // The runtime statistics are the primary signal, so a broken wait read must not suppress them...
            CapturingLogger.LogEntry entry = Assert.Single(logger.SlowQueryEntries);
            Assert.Equal(LogLevel.Information, entry.Level);
            Assert.Equal(slowQuery.QueryId, entry.Properties["QueryId"]);
            Assert.Equal(slowQuery.PlanId, entry.Properties["PlanId"]);
            Assert.Equal(slowQuery.TotalDurationMilliseconds, entry.Properties["TotalDurationMilliseconds"]);

            // ...and the breakage must be visible on the emitted line rather than looking like "this plan waited on
            // nothing", which is what an Unavailable status would mean. These are asserted on the structured state
            // rather than on the formatted message because a null renders as an empty string once formatted, which
            // is indistinguishable from a value that was genuinely reported as empty.
            Assert.Equal(QueryStoreDiagnosticsWatchdog.WaitStatisticsFailedStatus, entry.Properties["WaitStatisticsStatus"]);
            Assert.Null(entry.Properties["TotalWaitMilliseconds"]);
            Assert.Null(entry.Properties["AverageWaitMilliseconds"]);
            Assert.Null(entry.Properties["TopWaitCategory"]);

            // The failure itself is still reported, so that a wait read broken for a month is not visible only to
            // someone who thought to look at the status field.
            Assert.Contains(
                logger.WarningMessages,
                message => message.Contains(expectedWarning, StringComparison.Ordinal));
        }

        private static void ConfigureSlowQuery(ISqlRetryService sqlRetryService, QueryStoreDiagnosticsWatchdog.SlowQueryResult slowQuery)
        {
            sqlRetryService
                .ExecuteReaderAsync(
                    Arg.Any<SqlCommand>(),
                    Arg.Any<Func<SqlDataReader, QueryStoreDiagnosticsWatchdog.SlowQueryResult>>(),
                    Arg.Any<ILogger>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>(),
                    Arg.Any<bool>())
                .Returns(new List<QueryStoreDiagnosticsWatchdog.SlowQueryResult> { slowQuery });
        }

        private static void ConfigureStalledWait(ISqlRetryService sqlRetryService, TaskCompletionSource<bool> waitStarted = null)
        {
            sqlRetryService
                .ExecuteReaderAsync(
                    Arg.Any<SqlCommand>(),
                    Arg.Any<Func<SqlDataReader, QueryStoreDiagnosticsWatchdog.WaitStatistics>>(),
                    Arg.Any<ILogger>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>(),
                    Arg.Any<bool>())
                .Returns(call => WaitUntilCancelledAsync(call.ArgAt<CancellationToken>(4), waitStarted));
        }

        private static async Task<IReadOnlyList<QueryStoreDiagnosticsWatchdog.WaitStatistics>> WaitUntilCancelledAsync(CancellationToken cancellationToken, TaskCompletionSource<bool> waitStarted)
        {
            waitStarted?.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Array.Empty<QueryStoreDiagnosticsWatchdog.WaitStatistics>();
        }

        private static QueryStoreDiagnosticsWatchdog CreateWatchdog(ISqlRetryService sqlRetryService, ILogger<QueryStoreDiagnosticsWatchdog> logger, int collectionTimeoutSec = 30, int waitStatisticsTimeoutSec = 5)
        {
            var configuration = new WatchdogConfiguration();
            configuration.QueryStoreDiagnostics.Enabled = true;
            configuration.QueryStoreDiagnostics.SlowQueryCount = 10;
            configuration.QueryStoreDiagnostics.MinDurationMilliseconds = 1;
            configuration.QueryStoreDiagnostics.CollectionTimeoutSec = collectionTimeoutSec;
            configuration.QueryStoreDiagnostics.WaitStatisticsTimeoutSec = waitStatisticsTimeoutSec;

            // The plan and statistics sections are turned off so that the only reads this test has to stand up are
            // the two it is about.
            configuration.QueryStoreDiagnostics.IncludeQueryPlans = false;
            configuration.QueryStoreDiagnostics.IncludeStatisticsHealth = false;

            return new QueryStoreDiagnosticsWatchdog(
                sqlRetryService,
                logger,
                Options.Create(configuration),
                new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max) { Current = SchemaVersionConstants.Max });
        }

        /// <summary>
        /// Records what was logged, at what level, keeping the named properties alongside the formatted message so a
        /// test can assert on the values an emitted line carries rather than only on the text it renders to.
        /// </summary>
        private sealed class CapturingLogger : ILogger<QueryStoreDiagnosticsWatchdog>
        {
            private readonly List<LogEntry> _entries = new List<LogEntry>();

            internal IReadOnlyList<string> WarningMessages =>
                _entries.Where(entry => entry.Level == LogLevel.Warning).Select(entry => entry.Message).ToList();

            internal IReadOnlyList<string> Messages => _entries.Select(entry => entry.Message).ToList();

            internal IReadOnlyList<LogEntry> SlowQueryEntries =>
                _entries.Where(entry => entry.Message.StartsWith("QueryStoreDiagnosticsWatchdog slow query.", StringComparison.Ordinal)).ToList();

            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull
                => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                _entries.Add(new LogEntry(logLevel, formatter(state, exception), state as IReadOnlyList<KeyValuePair<string, object>>));
            }

            internal sealed class LogEntry
            {
                internal LogEntry(LogLevel level, string message, IReadOnlyList<KeyValuePair<string, object>> state)
                {
                    Level = level;
                    Message = message;

                    var properties = new Dictionary<string, object>(StringComparer.Ordinal);
                    foreach (KeyValuePair<string, object> property in state ?? Array.Empty<KeyValuePair<string, object>>())
                    {
                        // Assigned rather than added, because a template is free to repeat a placeholder and this
                        // capture must not throw on a line it merely passes through.
                        properties[property.Key] = property.Value;
                    }

                    Properties = properties;
                }

                internal LogLevel Level { get; }

                internal string Message { get; }

                internal IReadOnlyDictionary<string, object> Properties { get; }
            }
        }
    }
}
