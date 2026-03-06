// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Watchdogs
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class ExpiredResourceCleanupWatchdogTests
    {
        [Fact]
        public void GivenAWatchdog_WhenGettingParameterNames_ThenCorrectNamesAreReturned()
        {
            // Arrange
            var watchdog = new ExpiredResourceCleanupWatchdog();

            // Assert
            Assert.Equal("ExpiredResourceCleanupWatchdog.RetentionPeriodDays", watchdog.RetentionPeriodDaysId);
            Assert.Equal("ExpiredResourceCleanupWatchdog.IsEnabled", watchdog.IsEnabledId);
            Assert.Equal("ExpiredResourceCleanupWatchdog.DeleteOperation", watchdog.DeleteOperationId);
        }

        [Fact]
        public void GivenAWatchdog_WhenCheckingDefaultValues_ThenCorrectDefaultsAreSet()
        {
            // Arrange
            var watchdog = new ExpiredResourceCleanupWatchdog();

            // Assert
            Assert.Equal(2 * 3600, watchdog.PeriodSec); // 2 hours
            Assert.Equal(3600, watchdog.LeasePeriodSec); // 1 hour
            Assert.True(watchdog.AllowRebalance);
        }
    }
}
