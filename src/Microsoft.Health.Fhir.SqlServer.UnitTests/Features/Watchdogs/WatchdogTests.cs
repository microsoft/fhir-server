// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Watchdogs
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class WatchdogTests
    {
        [Theory]
        [InlineData(5, 4, false)]
        [InlineData(5, 5, true)]
        [InlineData(5, 6, true)]
        public void GivenLastRunTime_WhenCheckingDueTime_ThenOnlyPeriodElapsedRunsAreScheduled(int periodSeconds, int elapsedSeconds, bool expected)
        {
            var utcNow = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var lastRunTime = utcNow.AddSeconds(-elapsedSeconds);

            Assert.Equal(expected, Watchdog<ExpiredResourceCleanupWatchdog>.ShouldRun(utcNow, lastRunTime, TimeSpan.FromSeconds(periodSeconds)));
        }

        [Theory]
        [InlineData(60, 15, 15)]
        [InlineData(15, 60, 15)]
        [InlineData(30, 30, 30)]
        public void GivenLeasePeriodShorterThanWatchdogPeriod_WhenCalculatingTimerPeriod_ThenTimerUsesShorterInterval(int periodSeconds, int leasePeriodSeconds, int expectedTimerPeriodSeconds)
        {
            Assert.Equal(expectedTimerPeriodSeconds, Watchdog<ExpiredResourceCleanupWatchdog>.GetPeriod(periodSeconds, leasePeriodSeconds));
        }

        [Fact]
        public void GivenWatchdogPeriodLongerThanLeasePeriod_WhenCheckingDueTime_ThenWorkStillRunsOnlyAfterFullPeriodHasElapsed()
        {
            var utcNow = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var lastRunTime = utcNow.AddSeconds(-59);

            Assert.False(Watchdog<ExpiredResourceCleanupWatchdog>.ShouldRun(utcNow, lastRunTime, TimeSpan.FromSeconds(60)));

            lastRunTime = utcNow.AddSeconds(-60);
            Assert.True(Watchdog<ExpiredResourceCleanupWatchdog>.ShouldRun(utcNow, lastRunTime, TimeSpan.FromSeconds(60)));
        }

        [Fact]
        public async Task GivenWatchdogIsNotLeaseHolder_WhenTickRuns_ThenExistingSkipLogRunsInsideWatchdogActivity()
        {
            using var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == "Watchdog",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
            };
            ActivitySource.AddActivityListener(listener);

            var logger = new ActivityCapturingLogger<TraceTestWatchdog>();
            var watchdog = new TraceTestWatchdog(Substitute.For<ISqlRetryService>(), logger);

            await watchdog.TickAsync(CancellationToken.None);

            Assert.Contains("Skipping because watchdog is not a lease holder", logger.Message);
            Assert.NotNull(logger.Activity);
            Assert.Equal("Watchdog", logger.Activity.OperationName);
            Assert.Equal("Watchdog", logger.Activity.Source.Name);
            Assert.NotEqual(default, logger.Activity.TraceId);
            Assert.Null(Activity.Current);
        }

        private sealed class TraceTestWatchdog : Watchdog<TraceTestWatchdog>
        {
            public TraceTestWatchdog(ISqlRetryService sqlRetryService, ILogger<TraceTestWatchdog> logger)
                : base(sqlRetryService, logger)
            {
            }

            public override double LeasePeriodSec { get; internal set; } = 1;

            public override bool AllowRebalance { get; internal set; }

            public override double PeriodSec { get; internal set; } = 1;

            public Task TickAsync(CancellationToken cancellationToken)
            {
                return OnNextTickAsync(cancellationToken);
            }

            protected override Task RunWorkAsync(CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }
        }

        private sealed class ActivityCapturingLogger<T> : ILogger<T>
        {
            public string Message { get; private set; }

            public Activity Activity { get; private set; }

            public IDisposable BeginScope<TState>(TState state)
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                Message = formatter(state, exception);
                Activity = Activity.Current;
            }
        }
    }
}
