// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
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
        public void GivenLastRunDate_WhenCheckingDueTime_ThenOnlyPeriodElapsedRunsAreScheduled(int periodSeconds, int elapsedSeconds, bool expected)
        {
            var utcNow = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var lastRunDate = utcNow.AddSeconds(-elapsedSeconds);

            Assert.Equal(expected, Watchdog<ExpiredResourceCleanupWatchdog>.ShouldRun(utcNow, lastRunDate, TimeSpan.FromSeconds(periodSeconds)));
        }

        [Theory]
        [InlineData(60, 15, 15)]
        [InlineData(15, 60, 15)]
        [InlineData(30, 30, 30)]
        public void GivenLeasePeriodShorterThanWatchdogPeriod_WhenCalculatingTimerPeriod_ThenTimerUsesShorterInterval(int periodSeconds, int leasePeriodSeconds, int expectedTimerPeriodSeconds)
        {
            Assert.Equal(expectedTimerPeriodSeconds, Watchdog<ExpiredResourceCleanupWatchdog>.GetPeriodSec(periodSeconds, leasePeriodSeconds));
        }

        [Fact]
        public void GivenWatchdogPeriodLongerThanLeasePeriod_WhenCheckingDueTime_ThenWorkStillRunsOnlyAfterFullPeriodHasElapsed()
        {
            var utcNow = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var lastRunDate = utcNow.AddSeconds(-59);

            Assert.False(Watchdog<ExpiredResourceCleanupWatchdog>.ShouldRun(utcNow, lastRunDate, TimeSpan.FromSeconds(60)));

            lastRunDate = utcNow.AddSeconds(-60);
            Assert.True(Watchdog<ExpiredResourceCleanupWatchdog>.ShouldRun(utcNow, lastRunDate, TimeSpan.FromSeconds(60)));
        }
    }
}
