// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.JobMonitor;
using Microsoft.Health.Fhir.Core.Features.Operations.JobMonitor.Messages;
using Microsoft.Health.Fhir.Core.Logging.Metrics;
using Microsoft.Health.Fhir.Core.Logging.Metrics.Handlers;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Features.Operations.JobMonitor
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class JobMonitorWatchdogLogicTests
    {
        private static readonly DateTime _now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        private static JobMonitorMetricsNotification MakeNotification(
            Dictionary<QueueType, long> ages,
            Dictionary<QueueType, QueueDepth> depths = null)
        {
            return new JobMonitorMetricsNotification(
                ages,
                depths ?? new Dictionary<QueueType, QueueDepth>());
        }

        private static JobMonitorWatchdog.QueueStatusAggregate Created(QueueType queueType, DateTime oldestCreateDate, int count) =>
            new JobMonitorWatchdog.QueueStatusAggregate(queueType, JobStatus.Created, oldestCreateDate, count);

        private static JobMonitorWatchdog.QueueStatusAggregate Running(QueueType queueType, int count) =>
            new JobMonitorWatchdog.QueueStatusAggregate(queueType, JobStatus.Running, default, count);

        // ---- ComputeQueueAges ----

        [Fact]
        public void ComputeQueueAges_WhenRunningJobExists_StillReportsOldestCreatedAge_ZeroWhenNoCreatedJobs()
        {
            // Export has a Running job but no Created jobs — age is 0 because there are no Created jobs,
            // not because Running suppresses it.
            var aggregates = new List<JobMonitorWatchdog.QueueStatusAggregate>
            {
                Running(QueueType.Export, 1),
                Created(QueueType.Import, _now.AddMinutes(-20), 1),
            };

            var result = JobMonitorWatchdog.ComputeQueueAges(aggregates, _now);

            Assert.Equal(0, result[QueueType.Export]);
            Assert.Equal(20 * 60, result[QueueType.Import]);
        }

        [Fact]
        public void ComputeQueueAges_WhenNoJobsRunning_EmitsOldestCreatedJobAge()
        {
            // The aggregate row already carries MIN(CreateDate), so the oldest Created job is -754s.
            var aggregates = new List<JobMonitorWatchdog.QueueStatusAggregate>
            {
                Created(QueueType.Export, _now.AddSeconds(-754), 2),
            };

            var result = JobMonitorWatchdog.ComputeQueueAges(aggregates, _now);

            Assert.Equal(754.0, result[QueueType.Export]);
            Assert.Equal(0, result[QueueType.Import]);
        }

        [Fact]
        public void ComputeQueueAges_WhenRunningJobExists_StillReportsOldestCreatedAge()
        {
            // A Running job in the same queue must not suppress the Created job's age.
            var aggregates = new List<JobMonitorWatchdog.QueueStatusAggregate>
            {
                Running(QueueType.Export, 1),
                Created(QueueType.Export, _now.AddSeconds(-500), 1),
            };

            var result = JobMonitorWatchdog.ComputeQueueAges(aggregates, _now);

            Assert.Equal(500.0, result[QueueType.Export]);
        }

        [Fact]
        public void ComputeQueueAges_WhenQueueEmpty_EmitsZero()
        {
            // No rows for any queue: every monitored queue must still report 0.
            var aggregates = new List<JobMonitorWatchdog.QueueStatusAggregate>();

            var result = JobMonitorWatchdog.ComputeQueueAges(aggregates, _now);

            Assert.Equal(0, result[QueueType.Export]);
        }

        [Fact]
        public void ComputeQueueAges_WhenCreateDateInFuture_ClampsToZero()
        {
            // Clock skew: SQL-stamped CreateDate is ahead of the app-server utcNow.
            var aggregates = new List<JobMonitorWatchdog.QueueStatusAggregate>
            {
                Created(QueueType.Export, _now.AddSeconds(120), 1),
            };

            var result = JobMonitorWatchdog.ComputeQueueAges(aggregates, _now);

            Assert.Equal(0, result[QueueType.Export]);
        }

        [Fact]
        public void ComputeQueueAges_IsPerQueue_NotGlobal()
        {
            // A running job in one queue must NOT mask staleness in another queue.
            var aggregates = new List<JobMonitorWatchdog.QueueStatusAggregate>
            {
                Running(QueueType.Export, 1),
                Created(QueueType.Import, _now.AddSeconds(-900), 1),
            };

            var result = JobMonitorWatchdog.ComputeQueueAges(aggregates, _now);

            Assert.Equal(0, result[QueueType.Export]);
            Assert.Equal(900, result[QueueType.Import]);
        }

        // ---- ComputeQueueDepths ----

        [Fact]
        public void ComputeQueueDepths_WhenQueueEmpty_ReturnsZeroCounts()
        {
            var aggregates = new List<JobMonitorWatchdog.QueueStatusAggregate>();

            var result = JobMonitorWatchdog.ComputeQueueDepths(aggregates);

            Assert.Equal(new QueueDepth(0, 0), result[QueueType.Export]);
        }

        [Fact]
        public void ComputeQueueDepths_WhenOnlyPendingJobs_CountsPendingOnly()
        {
            var aggregates = new List<JobMonitorWatchdog.QueueStatusAggregate>
            {
                Created(QueueType.Export, _now, 3),
            };

            var result = JobMonitorWatchdog.ComputeQueueDepths(aggregates);

            Assert.Equal(new QueueDepth(Pending: 3, Running: 0), result[QueueType.Export]);
        }

        [Fact]
        public void ComputeQueueDepths_WhenOnlyRunningJobs_CountsRunningOnly()
        {
            var aggregates = new List<JobMonitorWatchdog.QueueStatusAggregate>
            {
                Running(QueueType.Import, 2),
            };

            var result = JobMonitorWatchdog.ComputeQueueDepths(aggregates);

            Assert.Equal(new QueueDepth(Pending: 0, Running: 2), result[QueueType.Import]);
        }

        [Fact]
        public void ComputeQueueDepths_WhenMixedJobs_CountsBothStates()
        {
            // The WHERE clause already excludes Completed/Failed/etc., so only Created+Running rows arrive.
            var aggregates = new List<JobMonitorWatchdog.QueueStatusAggregate>
            {
                Created(QueueType.Reindex, _now, 2),
                Running(QueueType.Reindex, 1),
            };

            var result = JobMonitorWatchdog.ComputeQueueDepths(aggregates);

            Assert.Equal(new QueueDepth(Pending: 2, Running: 1), result[QueueType.Reindex]);
        }

        [Fact]
        public void ComputeQueueDepths_IsPerQueue_NotGlobal()
        {
            // Running jobs in one queue must not affect counts in another.
            var aggregates = new List<JobMonitorWatchdog.QueueStatusAggregate>
            {
                Running(QueueType.Export, 1),
                Created(QueueType.Import, _now, 2),
            };

            var result = JobMonitorWatchdog.ComputeQueueDepths(aggregates);

            Assert.Equal(new QueueDepth(Pending: 0, Running: 1), result[QueueType.Export]);
            Assert.Equal(new QueueDepth(Pending: 2, Running: 0), result[QueueType.Import]);
        }

        // ---- JobMonitorMetricHandler (ages) ----

        [Fact]
        public async Task JobMonitorMetricHandler_Handle_UpdatesQueueAges()
        {
            var services = new ServiceCollection();
            services.AddMetrics();
            using var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<IMeterFactory>();
            var notificationHandler = new DefaultJobMonitorMetricHandler(factory);
            var notifier = new JobMonitorMetricNotifier(notificationHandler);

            var ages = new Dictionary<QueueType, long>
            {
                [QueueType.Export] = 754,
                [QueueType.Import] = 0,
            };

            await notifier.HandleAsync(MakeNotification(ages), CancellationToken.None);

            Assert.Equal(754.0, notifier.QueueAges[QueueType.Export]);
            Assert.Equal(0.0, notifier.QueueAges[QueueType.Import]);
        }

        [Fact]
        public async Task JobMonitorMetricHandler_Handle_OverwritesPreviousValues()
        {
            var services = new ServiceCollection();
            services.AddMetrics();
            using var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<IMeterFactory>();
            var notificationHandler = new DefaultJobMonitorMetricHandler(factory);
            var notifier = new JobMonitorMetricNotifier(notificationHandler);

            await notifier.HandleAsync(
                MakeNotification(new Dictionary<QueueType, long> { [QueueType.Export] = 500 }),
                CancellationToken.None);

            await notifier.HandleAsync(
                MakeNotification(new Dictionary<QueueType, long> { [QueueType.Export] = 0 }),
                CancellationToken.None);

            Assert.Equal(0.0, notifier.QueueAges[QueueType.Export]);
        }

        // ---- JobMonitorMetricHandler (depths) ----

        [Fact]
        public async Task JobMonitorMetricHandler_Handle_UpdatesQueueDepths()
        {
            var services = new ServiceCollection();
            services.AddMetrics();
            using var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<IMeterFactory>();
            var notificationHandler = new DefaultJobMonitorMetricHandler(factory);
            var notifier = new JobMonitorMetricNotifier(notificationHandler);

            var depths = new Dictionary<QueueType, QueueDepth>
            {
                [QueueType.Export] = new QueueDepth(Pending: 3, Running: 1),
                [QueueType.Import] = new QueueDepth(Pending: 0, Running: 0),
            };

            await notifier.HandleAsync(MakeNotification(new Dictionary<QueueType, long>(), depths), CancellationToken.None);

            Assert.Equal(new QueueDepth(3, 1), notifier.QueueDepths[QueueType.Export]);
            Assert.Equal(new QueueDepth(0, 0), notifier.QueueDepths[QueueType.Import]);
        }

        [Fact]
        public async Task JobMonitorMetricHandler_Handle_OverwritesPreviousDepths()
        {
            var services = new ServiceCollection();
            services.AddMetrics();
            using var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<IMeterFactory>();
            var notificationHandler = new DefaultJobMonitorMetricHandler(factory);
            var notifier = new JobMonitorMetricNotifier(notificationHandler);

            await notifier.HandleAsync(
                MakeNotification(
                    new Dictionary<QueueType, long>(),
                    new Dictionary<QueueType, QueueDepth> { [QueueType.Export] = new QueueDepth(5, 2) }),
                CancellationToken.None);

            await notifier.HandleAsync(
                MakeNotification(
                    new Dictionary<QueueType, long>(),
                    new Dictionary<QueueType, QueueDepth> { [QueueType.Export] = new QueueDepth(0, 0) }),
                CancellationToken.None);

            Assert.Equal(new QueueDepth(0, 0), notifier.QueueDepths[QueueType.Export]);
        }

        [Fact]
        public async Task JobMonitorMetricHandler_ObserveDepthValues_EmitsPendingAndRunningMeasurements()
        {
            var notificationHandler = Substitute.For<IJobMonitorMetricHandler>();
            var notifier = new JobMonitorMetricNotifier(notificationHandler);

            var depths = new Dictionary<QueueType, QueueDepth>
            {
                [QueueType.Export] = new QueueDepth(Pending: 4, Running: 2),
            };
            await notifier.HandleAsync(MakeNotification(new Dictionary<QueueType, long>(), depths), CancellationToken.None);

            notificationHandler.Received(1).ReportJobQueuePending("Export", 4);
            notificationHandler.Received(1).ReportJobQueueRunning("Export", 2);

            notificationHandler.DidNotReceive().ReportJobQueueAge(Arg.Any<string>(), Arg.Any<long>());
        }

        [Fact]
        public async Task JobMonitorMetricHandler_ObserveAgeValues_EmitsPerQueueMeasurementsWithQueueTypeTag()
        {
            var services = new ServiceCollection();
            services.AddMetrics();
            var notificationHandler = Substitute.For<IJobMonitorMetricHandler>();
            var notifier = new JobMonitorMetricNotifier(notificationHandler);

            var ages = new Dictionary<QueueType, long>
            {
                [QueueType.Export] = 754L,
                [QueueType.Import] = 120L,
            };
            var depths = new Dictionary<QueueType, QueueDepth>
            {
                [QueueType.Export] = new QueueDepth(Pending: 4, Running: 2),
                [QueueType.Import] = new QueueDepth(Pending: 5, Running: 1),
            };
            await notifier.HandleAsync(MakeNotification(ages, depths), CancellationToken.None);

            notificationHandler.Received(1).ReportJobQueueAge("Export", 754L);
            notificationHandler.Received(1).ReportJobQueueAge("Import", 120L);

            notificationHandler.Received(1).ReportJobQueuePending("Export", 4);
            notificationHandler.Received(1).ReportJobQueueRunning("Export", 2);

            notificationHandler.Received(1).ReportJobQueuePending("Import", 5);
            notificationHandler.Received(1).ReportJobQueueRunning("Import", 1);
        }

        [Fact]
        public async Task JobMonitorMetricHandler_ObserveAgeAndDepthValues_EmitsPerQueueMeasurementsWithQueueTypeTag()
        {
            var services = new ServiceCollection();
            services.AddMetrics();
            var notificationHandler = Substitute.For<IJobMonitorMetricHandler>();
            var notifier = new JobMonitorMetricNotifier(notificationHandler);

            var ages = new Dictionary<QueueType, long>
            {
                [QueueType.Export] = 754L,
                [QueueType.Import] = 120L,
            };

            await notifier.HandleAsync(MakeNotification(ages), CancellationToken.None);

            notificationHandler.Received(1).ReportJobQueueAge("Export", 754L);
            notificationHandler.Received(1).ReportJobQueueAge("Import", 120L);

            notificationHandler.DidNotReceive().ReportJobQueueRunning(Arg.Any<string>(), Arg.Any<long>());
        }

        [Fact]
        public async Task JobMonitorMetricHandler_Handle_ReplacesSnapshotNotMerges()
        {
            var notificationHandler = Substitute.For<IJobMonitorMetricHandler>();
            var notifier = new JobMonitorMetricNotifier(notificationHandler);

            // First snapshot: Export only.
            await notifier.HandleAsync(
                MakeNotification(new Dictionary<QueueType, long> { [QueueType.Export] = 100 }),
                CancellationToken.None);
            notificationHandler.Received(1).ReportJobQueueAge("Export", 100);
            notificationHandler.DidNotReceive().ReportJobQueuePending(Arg.Any<string>(), Arg.Any<long>());
            notificationHandler.DidNotReceive().ReportJobQueueRunning(Arg.Any<string>(), Arg.Any<long>());

            // Second snapshot: Import only — Export must not survive the replacement.
            await notifier.HandleAsync(
                MakeNotification(new Dictionary<QueueType, long> { [QueueType.Import] = 50 }),
                CancellationToken.None);
            notificationHandler.Received(1).ReportJobQueueAge("Import", 50);
            notificationHandler.DidNotReceive().ReportJobQueuePending(Arg.Any<string>(), Arg.Any<long>());
            notificationHandler.DidNotReceive().ReportJobQueueRunning(Arg.Any<string>(), Arg.Any<long>());

            Assert.False(notifier.QueueAges.ContainsKey(QueueType.Export), "Export key must not survive after snapshot replacement with Import-only data.");
            Assert.True(notifier.QueueAges.ContainsKey(QueueType.Import));
        }
    }
}
