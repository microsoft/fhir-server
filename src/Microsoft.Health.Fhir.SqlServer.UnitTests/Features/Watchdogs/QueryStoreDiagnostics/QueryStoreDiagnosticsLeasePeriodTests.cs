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
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs.QueryStoreDiagnostics;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Watchdogs.QueryStoreDiagnostics
{
    /// <summary>
    /// Covers the lease renewal interval. Like the collection period it is handed to a timer the base class owns,
    /// which rejects a non-positive or non-finite value, and a fault there cancels the token every watchdog shares —
    /// so a bad value in this off-by-default feature must degrade to the default rather than fail the host. It is
    /// also one of the two values written into <c>dbo.Parameters</c>, which is why it is on the configuration
    /// surface at all.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class QueryStoreDiagnosticsLeasePeriodTests
    {
        private const double DefaultLeasePeriodSec = 600;

        [Theory]
        [InlineData(0d)]
        [InlineData(-1d)]
        [InlineData(-600d)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void GivenAnUnusableConfiguredLeasePeriod_WhenConstructed_ThenTheDefaultIsUsedAndTheValueIsReported(double configuredLeasePeriodSec)
        {
            // Arrange
            var logger = new CapturingLogger();

            // Act
            QueryStoreDiagnosticsWatchdog watchdog = CreateWatchdog(logger, configuredLeasePeriodSec);

            // Assert
            // The lease period must never reach PeriodicTimer in this state: it throws there, which faults this
            // watchdog's task and causes WatchdogsBackgroundService to cancel the token every other watchdog shares.
            Assert.Equal(DefaultLeasePeriodSec, watchdog.LeasePeriodSec);

            string warning = Assert.Single(logger.WarningMessages);
            Assert.Contains(Format(configuredLeasePeriodSec), warning, StringComparison.Ordinal);
            Assert.Contains(Format(DefaultLeasePeriodSec), warning, StringComparison.Ordinal);
            Assert.Contains(QueryStoreDiagnosticsWatchdog.LeasePeriodSecConfigurationKey, warning, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(1d)]
        [InlineData(300d)]
        [InlineData(600d)]
        [InlineData(3600d)]
        public void GivenAUsableConfiguredLeasePeriod_WhenConstructed_ThenItIsUsedUnchangedAndNothingIsReported(double configuredLeasePeriodSec)
        {
            // Arrange
            var logger = new CapturingLogger();

            // Act
            QueryStoreDiagnosticsWatchdog watchdog = CreateWatchdog(logger, configuredLeasePeriodSec);

            // Assert
            Assert.Equal(configuredLeasePeriodSec, watchdog.LeasePeriodSec);
            Assert.Empty(logger.WarningMessages);
        }

        private static QueryStoreDiagnosticsWatchdog CreateWatchdog(ILogger<QueryStoreDiagnosticsWatchdog> logger, double configuredLeasePeriodSec)
        {
            var configuration = new WatchdogConfiguration();
            configuration.QueryStoreDiagnostics.Enabled = true;

            // Left at its valid class default so that the only value under test is the lease period; a bad period
            // would raise its own warning and defeat the Assert.Single checks above.
            configuration.QueryStoreDiagnostics.LeasePeriodSec = configuredLeasePeriodSec;

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
