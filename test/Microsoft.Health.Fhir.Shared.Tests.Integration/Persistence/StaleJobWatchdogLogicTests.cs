// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.StaleJob.Messages;
using Microsoft.Health.Fhir.Core.Logging.Metrics;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class StaleJobWatchdogLogicTests
    {
        private static DateTime _now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        private static StaleJobMetricsNotification MakeNotification(
            Dictionary<QueueType, double> ages,
            Dictionary<QueueType, QueueDepth> depths = null)
        {
            return new StaleJobMetricsNotification(
                ages,
                depths ?? new Dictionary<QueueType, QueueDepth>());
        }

        // ---- ComputeQueueAges ----

        [Fact]
        public void ComputeQueueAges_RunningJobSuppressesAgeInItsOwnQueue_OtherQueuesUnaffected()
        {
            var jobs = new Dictionary<QueueType, IReadOnlyList<JobInfo>>
            {
                [QueueType.Export] = new List<JobInfo>
                {
                    new JobInfo { Status = JobStatus.Running, CreateDate = _now.AddMinutes(-15) },
                },
                [QueueType.Import] = new List<JobInfo>
                {
                    new JobInfo { Status = JobStatus.Created, CreateDate = _now.AddMinutes(-20) },
                },
            };

            var result = StaleJobWatchdog.ComputeQueueAges(jobs, _now);

            Assert.Equal(0, result[QueueType.Export]);
            Assert.Equal(20 * 60, result[QueueType.Import]);
        }

        [Fact]
        public void ComputeQueueAges_WhenNoJobsRunning_EmitsOldestCreatedJobAge()
        {
            var jobs = new Dictionary<QueueType, IReadOnlyList<JobInfo>>
            {
                [QueueType.Export] = new List<JobInfo>
                {
                    new JobInfo { Status = JobStatus.Created, CreateDate = _now.AddSeconds(-754) },
                    new JobInfo { Status = JobStatus.Created, CreateDate = _now.AddSeconds(-200) },
                },
                [QueueType.Import] = new List<JobInfo>(),
            };

            var result = StaleJobWatchdog.ComputeQueueAges(jobs, _now);

            Assert.Equal(754.0, result[QueueType.Export]);
            Assert.Equal(0, result[QueueType.Import]);
        }

        [Fact]
        public void ComputeQueueAges_WhenQueueEmpty_EmitsZero()
        {
            var jobs = new Dictionary<QueueType, IReadOnlyList<JobInfo>>
            {
                [QueueType.Export] = new List<JobInfo>(),
            };

            var result = StaleJobWatchdog.ComputeQueueAges(jobs, _now);

            Assert.Equal(0, result[QueueType.Export]);
        }

        [Fact]
        public void ComputeQueueAges_IsPerQueue_NotGlobal()
        {
            // A running job in one queue must NOT mask staleness in another queue.
            var jobs = new Dictionary<QueueType, IReadOnlyList<JobInfo>>
            {
                [QueueType.Export] = new List<JobInfo>
                {
                    new JobInfo { Status = JobStatus.Running, CreateDate = _now.AddMinutes(-1) },
                },
                [QueueType.Import] = new List<JobInfo>
                {
                    new JobInfo { Status = JobStatus.Created, CreateDate = _now.AddSeconds(-900) },
                },
            };

            var result = StaleJobWatchdog.ComputeQueueAges(jobs, _now);

            Assert.Equal(0, result[QueueType.Export]);
            Assert.Equal(900, result[QueueType.Import]);
        }

        // ---- ComputeQueueDepths ----

        [Fact]
        public void ComputeQueueDepths_WhenQueueEmpty_ReturnsZeroCounts()
        {
            var jobs = new Dictionary<QueueType, IReadOnlyList<JobInfo>>
            {
                [QueueType.Export] = new List<JobInfo>(),
            };

            var result = StaleJobWatchdog.ComputeQueueDepths(jobs);

            Assert.Equal(new QueueDepth(0, 0), result[QueueType.Export]);
        }

        [Fact]
        public void ComputeQueueDepths_WhenOnlyPendingJobs_CountsPendingOnly()
        {
            var jobs = new Dictionary<QueueType, IReadOnlyList<JobInfo>>
            {
                [QueueType.Export] = new List<JobInfo>
                {
                    new JobInfo { Status = JobStatus.Created },
                    new JobInfo { Status = JobStatus.Created },
                    new JobInfo { Status = JobStatus.Created },
                },
            };

            var result = StaleJobWatchdog.ComputeQueueDepths(jobs);

            Assert.Equal(new QueueDepth(Pending: 3, Running: 0), result[QueueType.Export]);
        }

        [Fact]
        public void ComputeQueueDepths_WhenOnlyRunningJobs_CountsRunningOnly()
        {
            var jobs = new Dictionary<QueueType, IReadOnlyList<JobInfo>>
            {
                [QueueType.Import] = new List<JobInfo>
                {
                    new JobInfo { Status = JobStatus.Running },
                    new JobInfo { Status = JobStatus.Running },
                },
            };

            var result = StaleJobWatchdog.ComputeQueueDepths(jobs);

            Assert.Equal(new QueueDepth(Pending: 0, Running: 2), result[QueueType.Import]);
        }

        [Fact]
        public void ComputeQueueDepths_WhenMixedJobs_CountsBothStates()
        {
            var jobs = new Dictionary<QueueType, IReadOnlyList<JobInfo>>
            {
                [QueueType.Reindex] = new List<JobInfo>
                {
                    new JobInfo { Status = JobStatus.Created },
                    new JobInfo { Status = JobStatus.Created },
                    new JobInfo { Status = JobStatus.Running },
                    new JobInfo { Status = JobStatus.Completed },
                },
            };

            var result = StaleJobWatchdog.ComputeQueueDepths(jobs);

            Assert.Equal(new QueueDepth(Pending: 2, Running: 1), result[QueueType.Reindex]);
        }

        [Fact]
        public void ComputeQueueDepths_IsPerQueue_NotGlobal()
        {
            // Running jobs in one queue must not affect counts in another.
            var jobs = new Dictionary<QueueType, IReadOnlyList<JobInfo>>
            {
                [QueueType.Export] = new List<JobInfo>
                {
                    new JobInfo { Status = JobStatus.Running },
                },
                [QueueType.Import] = new List<JobInfo>
                {
                    new JobInfo { Status = JobStatus.Created },
                    new JobInfo { Status = JobStatus.Created },
                },
            };

            var result = StaleJobWatchdog.ComputeQueueDepths(jobs);

            Assert.Equal(new QueueDepth(Pending: 0, Running: 1), result[QueueType.Export]);
            Assert.Equal(new QueueDepth(Pending: 2, Running: 0), result[QueueType.Import]);
        }

        // ---- StaleJobMetricHandler (ages) ----

        [Fact]
        public async Task StaleJobMetricHandler_Handle_UpdatesQueueAges()
        {
            var services = new ServiceCollection();
            services.AddMetrics();
            using var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<IMeterFactory>();
            var handler = new StaleJobMetricHandler(factory);

            var ages = new Dictionary<QueueType, double>
            {
                [QueueType.Export] = 754.0,
                [QueueType.Import] = 0.0,
            };

            await handler.Handle(MakeNotification(ages), CancellationToken.None);

            Assert.Equal(754.0, handler.QueueAges[QueueType.Export]);
            Assert.Equal(0.0, handler.QueueAges[QueueType.Import]);
        }

        [Fact]
        public async Task StaleJobMetricHandler_Handle_OverwritesPreviousValues()
        {
            var services = new ServiceCollection();
            services.AddMetrics();
            using var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<IMeterFactory>();
            var handler = new StaleJobMetricHandler(factory);

            await handler.Handle(
                MakeNotification(new Dictionary<QueueType, double> { [QueueType.Export] = 500.0 }),
                CancellationToken.None);

            await handler.Handle(
                MakeNotification(new Dictionary<QueueType, double> { [QueueType.Export] = 0.0 }),
                CancellationToken.None);

            Assert.Equal(0.0, handler.QueueAges[QueueType.Export]);
        }

        // ---- StaleJobMetricHandler (depths) ----

        [Fact]
        public async Task StaleJobMetricHandler_Handle_UpdatesQueueDepths()
        {
            var services = new ServiceCollection();
            services.AddMetrics();
            using var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<IMeterFactory>();
            var handler = new StaleJobMetricHandler(factory);

            var depths = new Dictionary<QueueType, QueueDepth>
            {
                [QueueType.Export] = new QueueDepth(Pending: 3, Running: 1),
                [QueueType.Import] = new QueueDepth(Pending: 0, Running: 0),
            };

            await handler.Handle(MakeNotification(new Dictionary<QueueType, double>(), depths), CancellationToken.None);

            Assert.Equal(new QueueDepth(3, 1), handler.QueueDepths[QueueType.Export]);
            Assert.Equal(new QueueDepth(0, 0), handler.QueueDepths[QueueType.Import]);
        }

        [Fact]
        public async Task StaleJobMetricHandler_Handle_OverwritesPreviousDepths()
        {
            var services = new ServiceCollection();
            services.AddMetrics();
            using var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<IMeterFactory>();
            var handler = new StaleJobMetricHandler(factory);

            await handler.Handle(
                MakeNotification(
                    new Dictionary<QueueType, double>(),
                    new Dictionary<QueueType, QueueDepth> { [QueueType.Export] = new QueueDepth(5, 2) }),
                CancellationToken.None);

            await handler.Handle(
                MakeNotification(
                    new Dictionary<QueueType, double>(),
                    new Dictionary<QueueType, QueueDepth> { [QueueType.Export] = new QueueDepth(0, 0) }),
                CancellationToken.None);

            Assert.Equal(new QueueDepth(0, 0), handler.QueueDepths[QueueType.Export]);
        }

        [Fact]
        public async Task StaleJobMetricHandler_ObserveDepthValues_EmitsPendingAndRunningMeasurements()
        {
            var services = new ServiceCollection();
            services.AddMetrics();
            using var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<IMeterFactory>();
            var handler = new StaleJobMetricHandler(factory);

            var depths = new Dictionary<QueueType, QueueDepth>
            {
                [QueueType.Export] = new QueueDepth(Pending: 4, Running: 2),
            };
            await handler.Handle(MakeNotification(new Dictionary<QueueType, double>(), depths), CancellationToken.None);

            var collected = new List<(string Name, long Value, IDictionary<string, object> Tags)>();
            using var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Name == "Jobs.QueueDepth")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                var tagDict = new Dictionary<string, object>();
                foreach (var tag in tags)
                {
                    tagDict[tag.Key] = tag.Value;
                }

                collected.Add((instrument.Name, value, tagDict));
            });
            listener.Start();
            listener.RecordObservableInstruments();

            var exportMeasurements = collected.Where(m => m.Tags["queue_type"]?.ToString() == "Export").ToList();
            Assert.Equal(2, exportMeasurements.Count);

            var pending = exportMeasurements.Single(m => m.Tags["state"]?.ToString() == "pending");
            var running = exportMeasurements.Single(m => m.Tags["state"]?.ToString() == "running");
            Assert.Equal(4L, pending.Value);
            Assert.Equal(2L, running.Value);
        }
    }
}
