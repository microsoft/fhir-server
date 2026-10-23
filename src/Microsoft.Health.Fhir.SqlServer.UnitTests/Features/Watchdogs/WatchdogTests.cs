// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Watchdogs
{
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
    }
}
