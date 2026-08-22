// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Medino;
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
    /// Covers the optional run window, which decides whether a tick collects anything at all. Every boundary case is
    /// a clock comparison the integration tests cannot reach — they invoke the collection directly, and the window is
    /// evaluated above it — and the failure mode of getting one wrong is silence rather than an error, so the window
    /// has to be pinned here or not at all.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class QueryStoreDiagnosticsRunWindowTests
    {
        private const string WindowStart = "2026-03-01T00:00:00Z";
        private const string WindowEnd = "2026-03-08T00:00:00Z";

        [Theory]
        [InlineData("0001-01-01T00:00:00Z")]
        [InlineData("2026-03-01T00:00:00Z")]
        [InlineData("9999-12-31T23:59:59Z")]
        public void GivenNoConfiguredWindow_WhenCheckingAnyTime_ThenCollectionProceeds(string utcNow)
        {
            // Arrange
            var logger = new CapturingLogger();
            QueryStoreDiagnosticsWatchdog watchdog = CreateWatchdog(logger);

            // Act
            bool isWithinRunWindow = watchdog.IsWithinRunWindow(Parse(utcNow));

            // Assert
            // Both bounds unset is the default configuration, so this is the shape the feature has for every
            // deployment that never opts into a window.
            Assert.True(isWithinRunWindow);
            Assert.Empty(logger.WarningMessages);
        }

        [Theory]
        [InlineData("2026-02-28T23:59:59Z", false)]
        [InlineData("2026-03-01T00:00:00Z", true)] // the start bound is inclusive
        [InlineData("2026-03-01T00:00:01Z", true)]
        [InlineData("9999-12-31T23:59:59Z", true)]
        public void GivenOnlyAStartDate_WhenCheckingATime_ThenTheBoundIsInclusiveAndThereIsNoUpperLimit(string utcNow, bool expected)
        {
            // Arrange
            var logger = new CapturingLogger();
            QueryStoreDiagnosticsWatchdog watchdog = CreateWatchdog(logger, runStartDate: Parse(WindowStart));

            // Act
            bool isWithinRunWindow = watchdog.IsWithinRunWindow(Parse(utcNow));

            // Assert
            Assert.Equal(expected, isWithinRunWindow);
        }

        [Theory]
        [InlineData("0001-01-01T00:00:00Z", true)]
        [InlineData("2026-03-07T23:59:59Z", true)]
        [InlineData("2026-03-08T00:00:00Z", false)] // the end bound is exclusive
        [InlineData("2026-03-08T00:00:01Z", false)]
        public void GivenOnlyAnEndDate_WhenCheckingATime_ThenTheBoundIsExclusiveAndThereIsNoLowerLimit(string utcNow, bool expected)
        {
            // Arrange
            var logger = new CapturingLogger();
            QueryStoreDiagnosticsWatchdog watchdog = CreateWatchdog(logger, runEndDate: Parse(WindowEnd));

            // Act
            bool isWithinRunWindow = watchdog.IsWithinRunWindow(Parse(utcNow));

            // Assert
            Assert.Equal(expected, isWithinRunWindow);
        }

        [Theory]
        [InlineData("2026-02-28T23:59:59Z", false)]
        [InlineData("2026-03-01T00:00:00Z", true)]
        [InlineData("2026-03-04T12:00:00Z", true)]
        [InlineData("2026-03-07T23:59:59Z", true)]
        [InlineData("2026-03-08T00:00:00Z", false)]
        [InlineData("2026-03-09T00:00:00Z", false)]
        public void GivenBothDates_WhenCheckingATime_ThenOnlyTheHalfOpenIntervalCollects(string utcNow, bool expected)
        {
            // Arrange
            var logger = new CapturingLogger();
            QueryStoreDiagnosticsWatchdog watchdog = CreateWatchdog(logger, Parse(WindowStart), Parse(WindowEnd));

            // Act
            bool isWithinRunWindow = watchdog.IsWithinRunWindow(Parse(utcNow));

            // Assert
            Assert.Equal(expected, isWithinRunWindow);
        }

        [Theory]
        [InlineData(WindowEnd, WindowEnd, "2026-02-28T23:59:59Z")]
        [InlineData(WindowEnd, WindowEnd, "2026-03-08T00:00:00Z")]
        [InlineData(WindowEnd, WindowEnd, "9999-12-31T23:59:59Z")]
        [InlineData(WindowEnd, WindowStart, "2026-02-28T23:59:59Z")]
        [InlineData(WindowEnd, WindowStart, "2026-03-04T12:00:00Z")]
        [InlineData(WindowEnd, WindowStart, "2026-03-09T00:00:00Z")]
        public void GivenAStartDateNotBeforeTheEndDate_WhenCheckingAnyTime_ThenCollectionNeverProceeds(string runStartDate, string runEndDate, string utcNow)
        {
            // Arrange
            // Start equal to end is as empty as start after end, because the end bound is exclusive: there is no
            // instant that is both at or after the start and strictly before the end.
            var logger = new CapturingLogger();
            QueryStoreDiagnosticsWatchdog watchdog = CreateWatchdog(logger, Parse(runStartDate), Parse(runEndDate));

            // Act
            bool isWithinRunWindow = watchdog.IsWithinRunWindow(Parse(utcNow));

            // Assert
            Assert.False(isWithinRunWindow);
        }

        [Fact]
        public void GivenABoundWithANonUtcOffset_WhenCheckingATime_ThenItIsComparedAsTheInstantItDenotes()
        {
            // Arrange
            // 05:00+05:00 is midnight UTC. A bound configured without an explicit offset is bound in the host's local
            // timezone and arrives here exactly like this, so the comparison has to be on the instant rather than on
            // the wall-clock reading, or such a window opens hours early or late.
            var logger = new CapturingLogger();
            QueryStoreDiagnosticsWatchdog watchdog = CreateWatchdog(
                logger,
                runStartDate: new DateTimeOffset(2026, 3, 1, 5, 0, 0, TimeSpan.FromHours(5)));

            // Act
            bool beforeTheInstant = watchdog.IsWithinRunWindow(Parse("2026-02-28T23:59:59Z"));
            bool atTheInstant = watchdog.IsWithinRunWindow(Parse("2026-03-01T00:00:00Z"));

            // Assert
            Assert.False(beforeTheInstant);
            Assert.True(atTheInstant);
        }

        [Fact]
        public void GivenRepeatedTicksInTheSameState_WhenCheckingTheWindow_ThenOnlyTheTransitionsAreReported()
        {
            // Arrange
            // At the default hourly period a window that opens in a month would otherwise log around 720 identical
            // skip lines, which is what makes the skip unreadable rather than informative.
            var logger = new CapturingLogger();
            QueryStoreDiagnosticsWatchdog watchdog = CreateWatchdog(logger, Parse(WindowStart), Parse(WindowEnd));

            // Act
            watchdog.IsWithinRunWindow(Parse("2026-02-27T00:00:00Z"));
            watchdog.IsWithinRunWindow(Parse("2026-02-28T00:00:00Z"));
            watchdog.IsWithinRunWindow(Parse("2026-03-02T00:00:00Z"));
            watchdog.IsWithinRunWindow(Parse("2026-03-03T00:00:00Z"));
            watchdog.IsWithinRunWindow(Parse("2026-03-09T00:00:00Z"));
            watchdog.IsWithinRunWindow(Parse("2026-03-10T00:00:00Z"));

            // Assert
            // Three transitions across six ticks: not open yet, open, closed.
            Assert.Equal(3, logger.InformationMessages.Count);
            Assert.Contains("has not opened yet", logger.InformationMessages[0], StringComparison.Ordinal);
            Assert.Contains("within the configured run window", logger.InformationMessages[1], StringComparison.Ordinal);
            Assert.Contains("closed", logger.InformationMessages[2], StringComparison.Ordinal);

            // The skip is the feature working as configured, so none of it is a warning.
            Assert.Empty(logger.WarningMessages);
        }

        [Fact]
        public void GivenTheFirstObservedTick_WhenTheWindowIsAlreadyOpen_ThenTheStateIsStillReportedOnce()
        {
            // Arrange
            // The initial state has to log, or a process that starts inside its window reports nothing about the
            // window until the moment it closes.
            var logger = new CapturingLogger();
            QueryStoreDiagnosticsWatchdog watchdog = CreateWatchdog(logger, Parse(WindowStart), Parse(WindowEnd));

            // Act
            watchdog.IsWithinRunWindow(Parse("2026-03-02T00:00:00Z"));
            watchdog.IsWithinRunWindow(Parse("2026-03-03T00:00:00Z"));

            // Assert
            string message = Assert.Single(logger.InformationMessages);
            Assert.Contains(FormatUtc(Parse(WindowStart)), message, StringComparison.Ordinal);
            Assert.Contains(FormatUtc(Parse(WindowEnd)), message, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenAnEmptyWindow_WhenInitialized_ThenItIsReportedAsCollectingNothingEver()
        {
            // Arrange
            var logger = new CapturingLogger();
            QueryStoreDiagnosticsWatchdog watchdog = CreateWatchdog(logger, Parse(WindowEnd), Parse(WindowStart));

            // Act
            watchdog.ReportConfiguredRunWindow();

            // Assert
            // Nothing downstream ever complains about this configuration — it simply never collects — so the warning
            // has to name both values rather than say the window is invalid.
            string warning = Assert.Single(logger.WarningMessages);
            Assert.Contains(FormatUtc(Parse(WindowEnd)), warning, StringComparison.Ordinal);
            Assert.Contains(FormatUtc(Parse(WindowStart)), warning, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenAUsableWindow_WhenInitialized_ThenTheEffectiveWindowIsEchoedInUtcAndNothingIsWarned()
        {
            // Arrange
            // A bound typed without an offset resolves against the host's timezone and looks identical in
            // configuration either way, so the resolved UTC instants are echoed at startup where the mistake is still
            // cheap to correct.
            var logger = new CapturingLogger();
            QueryStoreDiagnosticsWatchdog watchdog = CreateWatchdog(
                logger,
                new DateTimeOffset(2026, 3, 1, 5, 0, 0, TimeSpan.FromHours(5)),
                new DateTimeOffset(2026, 3, 7, 19, 0, 0, TimeSpan.FromHours(-5)));

            // Act
            watchdog.ReportConfiguredRunWindow();

            // Assert
            string message = Assert.Single(logger.InformationMessages);
            Assert.Contains(FormatUtc(Parse(WindowStart)), message, StringComparison.Ordinal);
            Assert.Contains(FormatUtc(Parse(WindowEnd)), message, StringComparison.Ordinal);
            Assert.Empty(logger.WarningMessages);
        }

        [Fact]
        public void GivenNoConfiguredWindow_WhenInitialized_ThenNothingIsReported()
        {
            // Arrange
            var logger = new CapturingLogger();
            QueryStoreDiagnosticsWatchdog watchdog = CreateWatchdog(logger);

            // Act
            watchdog.ReportConfiguredRunWindow();

            // Assert
            // The default configuration has no window, and reporting an absent one on every host start would be noise.
            Assert.Empty(logger.InformationMessages);
            Assert.Empty(logger.WarningMessages);
        }

        private static QueryStoreDiagnosticsWatchdog CreateWatchdog(
            CapturingLogger logger,
            DateTimeOffset? runStartDate = null,
            DateTimeOffset? runEndDate = null)
        {
            var configuration = new WatchdogConfiguration();
            configuration.QueryStoreDiagnostics.Enabled = true;
            configuration.QueryStoreDiagnostics.RunStartDate = runStartDate;
            configuration.QueryStoreDiagnostics.RunEndDate = runEndDate;

            var watchdog = new QueryStoreDiagnosticsWatchdog(
                Substitute.For<ISqlRetryService>(),
                logger,
                Substitute.For<IMediator>(),
                Options.Create(configuration));

            // The shared base constructs a WatchdogLease, which logs through this same logger, so construction is not
            // silent. Dropping that here keeps every assertion below a statement about what the run window reported.
            logger.Clear();

            return watchdog;
        }

        private static DateTimeOffset Parse(string value) =>
            DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        // The logging infrastructure formats message arguments with the invariant culture, so expected values are
        // formatted the same way rather than with whatever culture the test host happens to run under.
        private static string FormatUtc(DateTimeOffset value) =>
            value.ToUniversalTime().ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Records what was logged, at what level, with the arguments already substituted, so a test can assert that
        /// an operator is told the values they need rather than merely that something was logged.
        /// </summary>
        private sealed class CapturingLogger : ILogger<QueryStoreDiagnosticsWatchdog>
        {
            private readonly List<(LogLevel Level, string Message)> _entries = new List<(LogLevel Level, string Message)>();

            internal IReadOnlyList<string> WarningMessages =>
                _entries.Where(entry => entry.Level == LogLevel.Warning).Select(entry => entry.Message).ToList();

            internal IReadOnlyList<string> InformationMessages =>
                _entries.Where(entry => entry.Level == LogLevel.Information).Select(entry => entry.Message).ToList();

            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull
                => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                _entries.Add((logLevel, formatter(state, exception)));
            }

            internal void Clear() => _entries.Clear();
        }
    }
}
