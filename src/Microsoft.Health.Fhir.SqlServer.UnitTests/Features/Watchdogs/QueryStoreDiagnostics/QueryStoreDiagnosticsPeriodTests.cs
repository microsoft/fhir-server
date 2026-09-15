// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs.QueryStoreDiagnostics;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs.QueryStoreDiagnostics.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Watchdogs.QueryStoreDiagnostics
{
    /// <summary>
    /// Covers the collection period, which is the one setting whose misconfiguration reaches outside this feature:
    /// it is handed to the timer this watchdog owns, and it is clamped independently when it is used as the Query
    /// Store lookback window. Neither path is reachable from the integration tests, which call the collection
    /// directly and never start the timer.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class QueryStoreDiagnosticsPeriodTests
    {
        private const double DefaultPeriodSec = 3600;

        [Theory]
        [InlineData(0d)]
        [InlineData(-1d)]
        [InlineData(-3600d)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void GivenAnUnusableConfiguredPeriod_WhenConstructed_ThenTheDefaultIsUsedAndTheValueIsReported(double configuredPeriodSec)
        {
            // Arrange
            var logger = new CapturingLogger();

            // Act
            QueryStoreDiagnosticsWatchdog watchdog = CreateWatchdog(logger, configuredPeriodSec);

            // Assert
            // The period must never reach PeriodicTimer in this state: it throws there, which faults this watchdog's
            // task and causes WatchdogsBackgroundService to cancel the token every other watchdog shares.
            Assert.Equal(DefaultPeriodSec, watchdog.PeriodSec);

            string warning = Assert.Single(logger.WarningMessages);
            Assert.Contains(Format(configuredPeriodSec), warning, StringComparison.Ordinal);
            Assert.Contains(Format(DefaultPeriodSec), warning, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(1d)]
        [InlineData(900d)]
        [InlineData(86400d)]
        public void GivenAUsableConfiguredPeriod_WhenConstructed_ThenItIsUsedUnchangedAndNothingIsReported(double configuredPeriodSec)
        {
            // Arrange
            var logger = new CapturingLogger();

            // Act
            QueryStoreDiagnosticsWatchdog watchdog = CreateWatchdog(logger, configuredPeriodSec);

            // Assert
            Assert.Equal(configuredPeriodSec, watchdog.PeriodSec);
            Assert.Empty(logger.WarningMessages);
        }

        [Fact]
        public void GivenAConfiguredPeriodAboveTheLookbackCap_WhenDerivingTheLookback_ThenTheUnexaminedWindowIsReported()
        {
            // Arrange
            const double configuredPeriodSec = 604800; // one week
            var logger = new CapturingLogger();
            QueryStoreDiagnosticsWatchdog watchdog = CreateWatchdog(logger, configuredPeriodSec);

            // Act
            double lookbackPeriodSec = watchdog.GetLookbackPeriodSec(watchdog.PeriodSec);

            // Assert
            Assert.Equal(86400d, lookbackPeriodSec);

            // The tick interval stays at the configured period, so the difference is a permanent coverage gap and the
            // warning has to name it rather than leave it to be worked out from the clamp.
            string warning = Assert.Single(logger.WarningMessages);
            Assert.Contains(Format(configuredPeriodSec), warning, StringComparison.Ordinal);
            Assert.Contains(Format(86400d), warning, StringComparison.Ordinal);
            Assert.Contains(Format(configuredPeriodSec - 86400d), warning, StringComparison.Ordinal);

            // The remedy names the configuration key, because there is no longer a database row to update.
            Assert.Contains(QueryStoreDiagnosticsWatchdog.PeriodSecConfigurationKey, warning, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenAConfiguredPeriodBelowTheLookbackFloor_WhenDerivingTheLookback_ThenTheOverlapIsReported()
        {
            // Arrange
            const double configuredPeriodSec = 30;
            var logger = new CapturingLogger();
            QueryStoreDiagnosticsWatchdog watchdog = CreateWatchdog(logger, configuredPeriodSec);

            // Act
            double lookbackPeriodSec = watchdog.GetLookbackPeriodSec(watchdog.PeriodSec);

            // Assert
            Assert.Equal(60d, lookbackPeriodSec);

            string warning = Assert.Single(logger.WarningMessages);
            Assert.Contains(Format(configuredPeriodSec), warning, StringComparison.Ordinal);
            Assert.Contains(Format(60d), warning, StringComparison.Ordinal);
            Assert.Contains("overlap", warning, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(QueryStoreDiagnosticsWatchdog.PeriodSecConfigurationKey, warning, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(60d)]
        [InlineData(3600d)]
        [InlineData(86400d)]
        public void GivenAConfiguredPeriodWithinTheLookbackRange_WhenDerivingTheLookback_ThenItIsUsedUnchangedAndNothingIsReported(double configuredPeriodSec)
        {
            // Arrange
            var logger = new CapturingLogger();
            QueryStoreDiagnosticsWatchdog watchdog = CreateWatchdog(logger, configuredPeriodSec);

            // Act
            double lookbackPeriodSec = watchdog.GetLookbackPeriodSec(watchdog.PeriodSec);

            // Assert
            Assert.Equal(configuredPeriodSec, lookbackPeriodSec);
            Assert.Empty(logger.WarningMessages);
        }

        private static QueryStoreDiagnosticsWatchdog CreateWatchdog(ILogger<QueryStoreDiagnosticsWatchdog> logger, double configuredPeriodSec)
        {
            var configuration = new WatchdogConfiguration();
            configuration.QueryStoreDiagnostics.Enabled = true;
            configuration.QueryStoreDiagnostics.PeriodSec = configuredPeriodSec;

            return new QueryStoreDiagnosticsWatchdog(
                Substitute.For<ISqlRetryService>(),
                logger,
                Options.Create(configuration));
        }

        // The logging infrastructure formats message arguments with the invariant culture, so expected values are
        // formatted the same way rather than with whatever culture the test host happens to run under.
        private static string Format(double value) => value.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Records what was logged, at what level, with the arguments already substituted, so a test can assert that
        /// an operator is told the values they need rather than merely that some warning was raised.
        /// </summary>
        private sealed class CapturingLogger : ILogger<QueryStoreDiagnosticsWatchdog>
        {
            private readonly List<(LogLevel Level, string Message)> _entries = new List<(LogLevel Level, string Message)>();

            internal IReadOnlyList<string> WarningMessages =>
                _entries.Where(entry => entry.Level == LogLevel.Warning).Select(entry => entry.Message).ToList();

            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull
                => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                _entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
