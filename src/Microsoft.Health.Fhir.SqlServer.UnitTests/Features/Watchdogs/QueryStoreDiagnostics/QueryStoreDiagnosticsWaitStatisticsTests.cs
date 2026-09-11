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
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs.QueryStoreDiagnostics;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs.QueryStoreDiagnostics.Models;
using Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
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
        private const int DeadlockErrorNumber = 1205;

        [Fact]
        public async Task GivenAFailingWaitStatisticsRead_WhenCollecting_ThenSlowQueriesAreLoggedWithFailedWaitStatus()
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
                    _ => throw SqlExceptionFactory.GetSqlException(DeadlockErrorNumber, "Transaction was deadlocked on lock resources."));

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
                message => message.Contains("wait statistics could not be read", StringComparison.Ordinal));
        }

        private static QueryStoreDiagnosticsWatchdog CreateWatchdog(ISqlRetryService sqlRetryService, ILogger<QueryStoreDiagnosticsWatchdog> logger)
        {
            var configuration = new WatchdogConfiguration();
            configuration.QueryStoreDiagnostics.Enabled = true;
            configuration.QueryStoreDiagnostics.SlowQueryCount = 10;
            configuration.QueryStoreDiagnostics.MinDurationMilliseconds = 1;

            // The plan and statistics sections are turned off so that the only reads this test has to stand up are
            // the two it is about.
            configuration.QueryStoreDiagnostics.IncludeQueryPlans = false;
            configuration.QueryStoreDiagnostics.IncludeStatisticsHealth = false;

            return new QueryStoreDiagnosticsWatchdog(
                sqlRetryService,
                logger,
                Options.Create(configuration));
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
