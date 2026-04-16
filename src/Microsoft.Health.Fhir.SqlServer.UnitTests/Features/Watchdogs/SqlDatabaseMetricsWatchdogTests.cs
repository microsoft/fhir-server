// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.SqlServer.Features.Metrics;
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
    public class SqlDatabaseMetricsWatchdogTests
    {
        [Fact]
        public void GivenAWatchdog_WhenCheckingDefaultValues_ThenCorrectDefaultsAreSet()
        {
            var watchdog = new SqlDatabaseMetricsWatchdog();

            Assert.Equal(60, watchdog.PeriodSec);
            Assert.Equal(120, watchdog.LeasePeriodSec);
            Assert.False(watchdog.AllowRebalance);
        }

        [Fact]
        public async Task GivenWatchdogEnabled_WhenRunWorkAsyncIsCalled_ThenMetricsAreEmitted()
        {
            var reader = Substitute.For<ISqlDatabaseResourceStatsReader>();
            var metricHandler = Substitute.For<ISqlDatabaseResourceMetricHandler>();
            var stats = new SqlDatabaseResourceStats
            {
                EndTime = new DateTimeOffset(2026, 4, 16, 1, 30, 0, TimeSpan.Zero),
                CpuPercent = 10,
                DataIoPercent = 20,
                LogIoPercent = 30,
                MemoryPercent = 40,
                WorkersPercent = 50,
                SessionsPercent = 60,
                InstanceCpuPercent = 70,
                InstanceMemoryPercent = 80,
            };

            reader.GetLatestAsync(Arg.Any<CancellationToken>()).Returns(stats);

            var watchdog = CreateWatchdog(reader, metricHandler, enabled: true, periodSeconds: 75);

            await watchdog.RunWorkForTestingAsync(CancellationToken.None);

            Assert.Equal(75, watchdog.PeriodSec);
            metricHandler.Received(1).Emit(Arg.Is<SqlDatabaseResourceMetricNotification>(notification =>
                notification.EndTime == stats.EndTime &&
                notification.CpuPercent == stats.CpuPercent &&
                notification.DataIoPercent == stats.DataIoPercent &&
                notification.LogIoPercent == stats.LogIoPercent &&
                notification.MemoryPercent == stats.MemoryPercent &&
                notification.WorkersPercent == stats.WorkersPercent &&
                notification.SessionsPercent == stats.SessionsPercent &&
                notification.InstanceCpuPercent == stats.InstanceCpuPercent &&
                notification.InstanceMemoryPercent == stats.InstanceMemoryPercent));
        }

        [Fact]
        public async Task GivenWatchdogDisabled_WhenRunWorkAsyncIsCalled_ThenMetricsAreNotEmitted()
        {
            var reader = Substitute.For<ISqlDatabaseResourceStatsReader>();
            var metricHandler = Substitute.For<ISqlDatabaseResourceMetricHandler>();
            var watchdog = CreateWatchdog(reader, metricHandler, enabled: false);

            await watchdog.RunWorkForTestingAsync(CancellationToken.None);

            await reader.DidNotReceive().GetLatestAsync(Arg.Any<CancellationToken>());
            metricHandler.DidNotReceive().Emit(Arg.Any<SqlDatabaseResourceMetricNotification>());
        }

        [Fact]
        public async Task GivenReaderReturnsNoStats_WhenRunWorkAsyncIsCalled_ThenMetricsAreNotEmitted()
        {
            var reader = Substitute.For<ISqlDatabaseResourceStatsReader>();
            var metricHandler = Substitute.For<ISqlDatabaseResourceMetricHandler>();
            var watchdog = CreateWatchdog(reader, metricHandler, enabled: true);

            reader.GetLatestAsync(Arg.Any<CancellationToken>()).Returns((SqlDatabaseResourceStats)null);

            await watchdog.RunWorkForTestingAsync(CancellationToken.None);

            metricHandler.DidNotReceive().Emit(Arg.Any<SqlDatabaseResourceMetricNotification>());
        }

        [Fact]
        public async Task GivenReaderThrows_WhenRunWorkAsyncIsCalled_ThenTheExceptionIsSwallowed()
        {
            var reader = Substitute.For<ISqlDatabaseResourceStatsReader>();
            var metricHandler = Substitute.For<ISqlDatabaseResourceMetricHandler>();
            var watchdog = CreateWatchdog(reader, metricHandler, enabled: true);

            reader.GetLatestAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException<SqlDatabaseResourceStats>(new InvalidOperationException("boom")));

            await watchdog.RunWorkForTestingAsync(CancellationToken.None);

            metricHandler.DidNotReceive().Emit(Arg.Any<SqlDatabaseResourceMetricNotification>());
        }

        private static SqlDatabaseMetricsWatchdog CreateWatchdog(
            ISqlDatabaseResourceStatsReader reader,
            ISqlDatabaseResourceMetricHandler metricHandler,
            bool enabled,
            int periodSeconds = 60)
        {
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            var configuration = new WatchdogConfiguration();
            configuration.SqlMetrics.Enabled = enabled;
            configuration.SqlMetrics.PeriodSeconds = periodSeconds;

            return new SqlDatabaseMetricsWatchdog(
                sqlRetryService,
                reader,
                metricHandler,
                Options.Create(configuration),
                NullLogger<SqlDatabaseMetricsWatchdog>.Instance);
        }
    }
}
