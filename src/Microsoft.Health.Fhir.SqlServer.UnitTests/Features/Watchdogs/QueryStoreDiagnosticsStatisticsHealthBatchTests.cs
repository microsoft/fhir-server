// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Watchdogs
{
    /// <summary>
    /// Covers the statistics-health batching, which is the one place where what was collected and what reaches a log
    /// line can differ. The pagination fields are the only way a reader can tell a short final page from a set that
    /// was cut short, and an off-by-one in the page arithmetic silently drops or duplicates rows rather than failing,
    /// so every boundary is pinned here. The integration test runs against whatever row count the live schema
    /// happens to have and cannot pin any of them.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class QueryStoreDiagnosticsStatisticsHealthBatchTests
    {
        private const int DefaultBatchSize = 20;

        [Fact]
        public void GivenExactlyOneFullBatch_WhenEmitting_ThenOneLineCarriesEveryRow()
        {
            // Arrange
            var logger = new CapturingLogger();
            QueryStoreDiagnosticsWatchdog watchdog = CreateWatchdog(logger, batchSize: 5);

            // Act
            watchdog.LogStatisticsHealthBatches(CreateRows(5));

            // Assert
            CapturingLogger.LogEntry entry = Assert.Single(logger.StatisticsHealthEntries);
            Assert.Equal(LogLevel.Information, entry.Level);
            Assert.Equal(1, entry.Properties["StatisticsHealthPage"]);
            Assert.Equal(1, entry.Properties["StatisticsHealthPageCount"]);
            Assert.Equal(5, entry.Properties["StatisticsHealthPageRowCount"]);
            Assert.Equal(5, entry.Properties["StatisticsHealthRowCount"]);

            // A full batch must not spill into an empty second page, which is what a page count derived by dividing
            // and then unconditionally adding one would produce.
            AssertRowsAre(entry, 0, 5);
        }

        [Fact]
        public void GivenAPartialFinalPage_WhenEmitting_ThenTheLastLineReportsOnlyTheRowsItCarries()
        {
            // Arrange
            // Twelve rows at a batch size of five is two full pages and a final page of two.
            var logger = new CapturingLogger();
            QueryStoreDiagnosticsWatchdog watchdog = CreateWatchdog(logger, batchSize: 5);

            // Act
            watchdog.LogStatisticsHealthBatches(CreateRows(12));

            // Assert
            Assert.Equal(3, logger.StatisticsHealthEntries.Count);
            Assert.Equal(new[] { 1, 2, 3 }, logger.StatisticsHealthEntries.Select(entry => (int)entry.Properties["StatisticsHealthPage"]));
            Assert.All(logger.StatisticsHealthEntries, entry => Assert.Equal(3, entry.Properties["StatisticsHealthPageCount"]));

            // The whole point of carrying the total: a final page of two out of twelve is complete, and a reader has
            // to be able to say so without guessing from the batch size.
            Assert.All(logger.StatisticsHealthEntries, entry => Assert.Equal(12, entry.Properties["StatisticsHealthRowCount"]));
            Assert.Equal(new[] { 5, 5, 2 }, logger.StatisticsHealthEntries.Select(entry => (int)entry.Properties["StatisticsHealthPageRowCount"]));

            AssertRowsAre(logger.StatisticsHealthEntries[0], 0, 5);
            AssertRowsAre(logger.StatisticsHealthEntries[1], 5, 5);
            AssertRowsAre(logger.StatisticsHealthEntries[2], 10, 2);
        }

        [Fact]
        public void GivenMoreRowsThanOneBatch_WhenEmitting_ThenEveryRowIsEmittedExactlyOnceInOrder()
        {
            // Arrange
            var logger = new CapturingLogger();
            QueryStoreDiagnosticsWatchdog watchdog = CreateWatchdog(logger, batchSize: 4);

            // Act
            watchdog.LogStatisticsHealthBatches(CreateRows(9));

            // Assert
            Assert.Equal(3, logger.StatisticsHealthEntries.Count);

            // Paging is what makes the batch safe, so the union of the pages has to be the collected set exactly:
            // neither a dropped row at a page boundary nor one emitted on two pages.
            List<string> emitted = logger.StatisticsHealthEntries
                .SelectMany(entry => DeserializeRows(entry).Select(row => row.StatisticsName))
                .ToList();
            Assert.Equal(CreateRows(9).Select(row => row.StatisticsName).ToList(), emitted);
        }

        [Fact]
        public void GivenANonPositiveBatchSize_WhenEmitting_ThenTheDefaultIsUsedAndTheValueIsReported()
        {
            // Arrange
            // Unlike the counts, a batch size of zero cannot mean "collect nothing": the rows have already been read
            // by this point, so degrading to the default is the only option that does not throw away diagnostics.
            var logger = new CapturingLogger();
            QueryStoreDiagnosticsWatchdog watchdog = CreateWatchdog(logger, batchSize: 0);

            // Act
            watchdog.LogStatisticsHealthBatches(CreateRows(DefaultBatchSize + 1));

            // Assert
            Assert.Equal(2, logger.StatisticsHealthEntries.Count);
            Assert.Equal(DefaultBatchSize, logger.StatisticsHealthEntries[0].Properties["StatisticsHealthPageRowCount"]);
            Assert.Equal(1, logger.StatisticsHealthEntries[1].Properties["StatisticsHealthPageRowCount"]);

            string warning = Assert.Single(logger.WarningMessages);
            Assert.Contains(Format(0), warning, StringComparison.Ordinal);
            Assert.Contains(Format(DefaultBatchSize), warning, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenABatchSizeAboveTheCap_WhenEmitting_ThenItIsClampedAndEveryRowIsStillEmitted()
        {
            // Arrange
            // An unbounded batch would rebuild the single oversized record that is the reason plan XML is never
            // batched, so the cap binds. Clamping must page the rows rather than drop the ones past the cap.
            const int MaxBatchSize = 64;
            var logger = new CapturingLogger();
            QueryStoreDiagnosticsWatchdog watchdog = CreateWatchdog(logger, batchSize: 5000);

            // Act
            watchdog.LogStatisticsHealthBatches(CreateRows(MaxBatchSize + 3));

            // Assert
            Assert.Equal(2, logger.StatisticsHealthEntries.Count);
            Assert.Equal(MaxBatchSize, logger.StatisticsHealthEntries[0].Properties["StatisticsHealthPageRowCount"]);
            Assert.Equal(3, logger.StatisticsHealthEntries[1].Properties["StatisticsHealthPageRowCount"]);
            Assert.Equal(MaxBatchSize + 3, logger.StatisticsHealthEntries[0].Properties["StatisticsHealthRowCount"]);

            AssertRowsAre(logger.StatisticsHealthEntries[0], firstRowIndex: 0, expectedRowCount: MaxBatchSize);
            AssertRowsAre(logger.StatisticsHealthEntries[1], firstRowIndex: MaxBatchSize, expectedRowCount: 3);

            string warning = Assert.Single(logger.WarningMessages);
            Assert.Contains(Format(5000), warning, StringComparison.Ordinal);
            Assert.Contains(Format(MaxBatchSize), warning, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenNoRows_WhenEmitting_ThenNoLineIsEmitted()
        {
            // Arrange
            // The collection summary already reports a count of zero, so an empty page would add nothing except an
            // extra page for a reader counting them.
            var logger = new CapturingLogger();
            QueryStoreDiagnosticsWatchdog watchdog = CreateWatchdog(logger, batchSize: 5);

            // Act
            watchdog.LogStatisticsHealthBatches(Array.Empty<StatisticsHealthDiagnostics>());

            // Assert
            Assert.Empty(logger.StatisticsHealthEntries);
            Assert.Empty(logger.WarningMessages);
        }

        private static void AssertRowsAre(CapturingLogger.LogEntry entry, int firstRowIndex, int expectedRowCount)
        {
            List<StatisticsHealthDiagnostics> rows = DeserializeRows(entry);
            Assert.Equal(expectedRowCount, rows.Count);
            for (int offset = 0; offset < expectedRowCount; offset++)
            {
                Assert.Equal(RowName(firstRowIndex + offset), rows[offset].StatisticsName);
            }
        }

        private static List<StatisticsHealthDiagnostics> DeserializeRows(CapturingLogger.LogEntry entry)
        {
            // Deserializing rather than string-matching, because the property is only useful downstream if it really
            // is a JSON array of rows.
            return JsonSerializer.Deserialize<List<StatisticsHealthDiagnostics>>((string)entry.Properties["StatisticsHealthRows"]);
        }

        private static IReadOnlyList<StatisticsHealthDiagnostics> CreateRows(int count)
        {
            return Enumerable.Range(0, count)
                .Select(index => new StatisticsHealthDiagnostics
                {
                    SchemaName = "dbo",
                    TableName = "Resource",
                    StatisticsName = RowName(index),
                    Rows = index,
                })
                .ToList();
        }

        private static string RowName(int index) => FormattableString.Invariant($"ST_{index}");

        // The logging infrastructure formats message arguments with the invariant culture, so expected values are
        // formatted the same way rather than with whatever culture the test host happens to run under.
        private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static QueryStoreDiagnosticsWatchdog CreateWatchdog(ILogger<QueryStoreDiagnosticsWatchdog> logger, int batchSize)
        {
            var configuration = new WatchdogConfiguration();
            configuration.QueryStoreDiagnostics.Enabled = true;
            configuration.QueryStoreDiagnostics.StatisticsHealthBatchSize = batchSize;

            return new QueryStoreDiagnosticsWatchdog(
                Substitute.For<ISqlRetryService>(),
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

            internal IReadOnlyList<LogEntry> StatisticsHealthEntries =>
                _entries.Where(entry => entry.Message.StartsWith("QueryStoreDiagnosticsWatchdog statistics health.", StringComparison.Ordinal)).ToList();

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
