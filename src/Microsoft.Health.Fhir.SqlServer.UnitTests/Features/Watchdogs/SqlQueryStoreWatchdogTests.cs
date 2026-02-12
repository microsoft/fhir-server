// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Watchdogs
{
    public class SqlQueryStoreWatchdogTests
    {
        [Fact]
        public void GivenASqlQueryStoreWatchdog_WhenGettingParameterNames_ThenCorrectNamesAreReturned()
        {
            var watchdog = new SqlQueryStoreWatchdog();

            Assert.Equal("SqlQueryStoreWatchdog", watchdog.Name);
            Assert.Equal("SqlQueryStoreWatchdog.PeriodSec", watchdog.PeriodSecId);
            Assert.Equal("SqlQueryStoreWatchdog.LeasePeriodSec", watchdog.LeasePeriodSecId);
        }

        [Fact]
        public void GivenASqlQueryStoreWatchdog_WhenGettingDefaultValues_ThenCorrectDefaultsAreReturned()
        {
            var watchdog = new SqlQueryStoreWatchdog();

            Assert.Equal(3600, watchdog.PeriodSec);
            Assert.Equal(3600, watchdog.LeasePeriodSec);
            Assert.True(watchdog.AllowRebalance);
        }
    }
}
