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
    /// Covers the collection period, which is the one setting whose misconfiguration reaches outside this feature:
    /// it is handed to the shared watchdog timer, it is silently overridden by <c>dbo.Parameters</c>, and it is
    /// clamped independently when it is used as the Query Store lookback window. None of those paths is reachable
    /// from the integration tests, which call the collection directly and never run initialization.
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
        public void GivenAStoredPeriodDifferentFromTheConfiguredOne_WhenInitialized_ThenTheSilentOverrideIsReported()
        {
            // Arrange
            var logger = new CapturingLogger();
            QueryStoreDiagnosticsWatchdog watchdog = CreateWatchdog(logger, configuredPeriodSec: DefaultPeriodSec);

            // dbo.Parameters is write-once in practice, so the base class overwrites the configured period with the
            // stored one during initialization. This is that overwrite.
            watchdog.PeriodSec = 900;

            // Act
            watchdog.WarnIfStoredPeriodSecOverridesConfiguration();

            // Assert
            string warning = Assert.Single(logger.WarningMessages);
            Assert.Contains(Format(DefaultPeriodSec), warning, StringComparison.Ordinal);
            Assert.Contains(Format(900d), warning, StringComparison.Ordinal);
            Assert.Contains(watchdog.PeriodSecId, warning, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenAStoredPeriodEqualToTheConfiguredOne_WhenInitialized_ThenNothingIsReported()
        {
            // Arrange
            var logger = new CapturingLogger();
            QueryStoreDiagnosticsWatchdog watchdog = CreateWatchdog(logger, configuredPeriodSec: 900);

            // Act
            watchdog.WarnIfStoredPeriodSecOverridesConfiguration();

            // Assert
            Assert.Empty(logger.WarningMessages);
        }

        [Fact]
        public void GivenAnUnusableConfiguredPeriod_WhenInitialized_ThenTheSubstitutedDefaultIsNotReportedAsAnOverride()
        {
            // Arrange
            // The rejected value was already reported at construction, and the default that replaced it is what got
            // seeded into dbo.Parameters, so reporting it again as an override would contradict the first warning.
            var logger = new CapturingLogger();
            QueryStoreDiagnosticsWatchdog watchdog = CreateWatchdog(logger, configuredPeriodSec: 0);
            logger.Clear();

            // Act
            watchdog.WarnIfStoredPeriodSecOverridesConfiguration();

            // Assert
            Assert.Empty(logger.WarningMessages);
        }

        [Fact]
        public void GivenAStoredPeriodAboveTheLookbackCap_WhenDerivingTheLookback_ThenTheUnexaminedWindowIsReported()
        {
            // Arrange
            const double storedPeriodSec = 604800; // one week
            var logger = new CapturingLogger();
            QueryStoreDiagnosticsWatchdog watchdog = CreateWatchdog(logger, configuredPeriodSec: DefaultPeriodSec);

            // Act
            double lookbackPeriodSec = watchdog.GetLookbackPeriodSec(storedPeriodSec);

            // Assert
            Assert.Equal(86400d, lookbackPeriodSec);

            // The tick interval stays at the stored period, so the difference is a permanent coverage gap and the
            // warning has to name it rather than leave it to be worked out from the clamp.
            string warning = Assert.Single(logger.WarningMessages);
            Assert.Contains(Format(storedPeriodSec), warning, StringComparison.Ordinal);
            Assert.Contains(Format(86400d), warning, StringComparison.Ordinal);
            Assert.Contains(Format(storedPeriodSec - 86400d), warning, StringComparison.Ordinal);
            Assert.Contains(watchdog.PeriodSecId, warning, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenAStoredPeriodBelowTheLookbackFloor_WhenDerivingTheLookback_ThenTheOverlapIsReported()
        {
            // Arrange
            const double storedPeriodSec = 30;
            var logger = new CapturingLogger();
            QueryStoreDiagnosticsWatchdog watchdog = CreateWatchdog(logger, configuredPeriodSec: DefaultPeriodSec);

            // Act
            double lookbackPeriodSec = watchdog.GetLookbackPeriodSec(storedPeriodSec);

            // Assert
            Assert.Equal(60d, lookbackPeriodSec);

            string warning = Assert.Single(logger.WarningMessages);
            Assert.Contains(Format(storedPeriodSec), warning, StringComparison.Ordinal);
            Assert.Contains(Format(60d), warning, StringComparison.Ordinal);
            Assert.Contains("overlap", warning, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData(60d)]
        [InlineData(3600d)]
        [InlineData(86400d)]
        public void GivenAStoredPeriodWithinTheLookbackRange_WhenDerivingTheLookback_ThenItIsUsedUnchangedAndNothingIsReported(double storedPeriodSec)
        {
            // Arrange
            var logger = new CapturingLogger();
            QueryStoreDiagnosticsWatchdog watchdog = CreateWatchdog(logger, configuredPeriodSec: DefaultPeriodSec);

            // Act
            double lookbackPeriodSec = watchdog.GetLookbackPeriodSec(storedPeriodSec);

            // Assert
            Assert.Equal(storedPeriodSec, lookbackPeriodSec);
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
                Substitute.For<IMediator>(),
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

            internal void Clear() => _entries.Clear();
        }
    }
}
