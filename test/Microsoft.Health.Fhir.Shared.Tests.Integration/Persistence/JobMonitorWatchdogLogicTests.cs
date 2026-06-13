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
using Microsoft.Extensions.Time.Testing;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.JobMonitor.Messages;
using Microsoft.Health.Fhir.Core.Logging.Metrics;
using Microsoft.Health.Fhir.Core.Logging.Metrics.Handlers;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class JobMonitorWatchdogLogicTests
    {
        private static readonly DateTime _now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        private static JobMonitorMetricsNotification MakeNotification(
            Dictionary<QueueType, double> ages,
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
            var handler = new JobMonitorMetricHandler(factory);

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
        public async Task JobMonitorMetricHandler_Handle_OverwritesPreviousValues()
        {
            var services = new ServiceCollection();
            services.AddMetrics();
            using var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<IMeterFactory>();
            var handler = new JobMonitorMetricHandler(factory);

            await handler.Handle(
                MakeNotification(new Dictionary<QueueType, double> { [QueueType.Export] = 500.0 }),
                CancellationToken.None);

            await handler.Handle(
                MakeNotification(new Dictionary<QueueType, double> { [QueueType.Export] = 0.0 }),
                CancellationToken.None);

            Assert.Equal(0.0, handler.QueueAges[QueueType.Export]);
        }

        // ---- JobMonitorMetricHandler (depths) ----

        [Fact]
        public async Task JobMonitorMetricHandler_Handle_UpdatesQueueDepths()
        {
            var services = new ServiceCollection();
            services.AddMetrics();
            using var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<IMeterFactory>();
            var handler = new JobMonitorMetricHandler(factory);

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
        public async Task JobMonitorMetricHandler_Handle_OverwritesPreviousDepths()
        {
            var services = new ServiceCollection();
            services.AddMetrics();
            using var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<IMeterFactory>();
            var handler = new JobMonitorMetricHandler(factory);

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
        public async Task JobMonitorMetricHandler_ObserveDepthValues_EmitsPendingAndRunningMeasurements()
        {
            var services = new ServiceCollection();
            services.AddMetrics();
            using var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<IMeterFactory>();
            var handler = new JobMonitorMetricHandler(factory);

            var depths = new Dictionary<QueueType, QueueDepth>
            {
                [QueueType.Export] = new QueueDepth(Pending: 4, Running: 2),
            };
            await handler.Handle(MakeNotification(new Dictionary<QueueType, double>(), depths), CancellationToken.None);

            var collected = new List<(string Name, long Value, IDictionary<string, object> Tags)>();
            using var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, l) =>
            {
                // Scope to this test's meter: every handler registers same-named instruments on the
                // process-global "FhirServer" meter name, so name-only filtering leaks across parallel tests.
                if (instrument.Name == "Jobs.QueueDepth" && ReferenceEquals(instrument.Meter.Scope, factory))
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

        [Fact]
        public async Task JobMonitorMetricHandler_ObserveAgeValues_EmitsPerQueueMeasurementsWithQueueTypeTag()
        {
            var fakeTime = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
            using (Mock.Property(() => ClockResolver.TimeProvider, fakeTime))
            {
                var services = new ServiceCollection();
                services.AddMetrics();
                using var provider = services.BuildServiceProvider();
                var factory = provider.GetRequiredService<IMeterFactory>();
                var handler = new JobMonitorMetricHandler(factory);

                var ages = new Dictionary<QueueType, double>
                {
                    [QueueType.Export] = 754.0,
                    [QueueType.Import] = 120.0,
                };
                await handler.Handle(MakeNotification(ages), CancellationToken.None);

                var collected = new List<(string Name, double Value, IDictionary<string, object> Tags)>();
                using var listener = new MeterListener();
                listener.InstrumentPublished = (instrument, l) =>
                {
                    if (instrument.Name == "Jobs.OldestQueuedAge" && ReferenceEquals(instrument.Meter.Scope, factory))
                    {
                        l.EnableMeasurementEvents(instrument);
                    }
                };
                listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
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

                Assert.Equal(2, collected.Count);
                Assert.All(collected, m => Assert.Equal("Jobs.OldestQueuedAge", m.Name));
                Assert.All(collected, m => Assert.True(m.Tags.ContainsKey("queue_type")));

                var exportMeasurement = collected.Single(m => m.Tags["queue_type"]?.ToString() == "Export");
                var importMeasurement = collected.Single(m => m.Tags["queue_type"]?.ToString() == "Import");
                Assert.Equal(754.0, exportMeasurement.Value);
                Assert.Equal(120.0, importMeasurement.Value);
            }
        }

        [Fact]
        public async Task JobMonitorMetricHandler_WhenSnapshotIsStale_EmitsNoMeasurements_ThenResumesAfterFreshPublish()
        {
            var fakeTime = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
            using (Mock.Property(() => ClockResolver.TimeProvider, fakeTime))
            {
                var services = new ServiceCollection();
                services.AddMetrics();
                using var provider = services.BuildServiceProvider();
                var factory = provider.GetRequiredService<IMeterFactory>();
                var handler = new JobMonitorMetricHandler(factory);

                var ages = new Dictionary<QueueType, double> { [QueueType.Export] = 60.0 };
                var depths = new Dictionary<QueueType, QueueDepth> { [QueueType.Export] = new QueueDepth(1, 0) };
                await handler.Handle(MakeNotification(ages, depths), CancellationToken.None);

                // Advance past the staleness cutoff.
                fakeTime.Advance(TimeSpan.FromSeconds(JobMonitorMetricHandler.SnapshotStaleCutoffSeconds + 1));

                var collectedStale = new List<(string Name, IDictionary<string, object> Tags)>();
                using var listenerStale = new MeterListener();
                listenerStale.InstrumentPublished = (instrument, l) =>
                {
                    if (instrument.Name is "Jobs.OldestQueuedAge" or "Jobs.QueueDepth" && ReferenceEquals(instrument.Meter.Scope, factory))
                    {
                        l.EnableMeasurementEvents(instrument);
                    }
                };
                listenerStale.SetMeasurementEventCallback<double>((instrument, _, tags, _) =>
                {
                    var tagDict = new Dictionary<string, object>();
                    foreach (var tag in tags)
                    {
                        tagDict[tag.Key] = tag.Value;
                    }

                    collectedStale.Add((instrument.Name, tagDict));
                });
                listenerStale.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
                {
                    var tagDict = new Dictionary<string, object>();
                    foreach (var tag in tags)
                    {
                        tagDict[tag.Key] = tag.Value;
                    }

                    collectedStale.Add((instrument.Name, tagDict));
                });
                listenerStale.Start();
                listenerStale.RecordObservableInstruments();

                Assert.Empty(collectedStale);

                // Publish a fresh snapshot; measurements must resume.
                await handler.Handle(MakeNotification(ages, depths), CancellationToken.None);

                var collectedFresh = new List<string>();
                using var listenerFresh = new MeterListener();
                listenerFresh.InstrumentPublished = (instrument, l) =>
                {
                    if (instrument.Name is "Jobs.OldestQueuedAge" or "Jobs.QueueDepth" && ReferenceEquals(instrument.Meter.Scope, factory))
                    {
                        l.EnableMeasurementEvents(instrument);
                    }
                };
                listenerFresh.SetMeasurementEventCallback<double>((instrument, _, _, _) => collectedFresh.Add(instrument.Name));
                listenerFresh.SetMeasurementEventCallback<long>((instrument, _, _, _) => collectedFresh.Add(instrument.Name));
                listenerFresh.Start();
                listenerFresh.RecordObservableInstruments();

                Assert.Contains("Jobs.OldestQueuedAge", collectedFresh);
                Assert.Contains("Jobs.QueueDepth", collectedFresh);
            }
        }

        [Fact]
        public async Task JobMonitorMetricHandler_Handle_ReplacesSnapshotNotMerges()
        {
            var fakeTime = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
            using (Mock.Property(() => ClockResolver.TimeProvider, fakeTime))
            {
                var services = new ServiceCollection();
                services.AddMetrics();
                using var provider = services.BuildServiceProvider();
                var factory = provider.GetRequiredService<IMeterFactory>();
                var handler = new JobMonitorMetricHandler(factory);

                // First snapshot: Export only.
                await handler.Handle(
                    MakeNotification(new Dictionary<QueueType, double> { [QueueType.Export] = 100.0 }),
                    CancellationToken.None);

                // Second snapshot: Import only — Export must not survive the replacement.
                await handler.Handle(
                    MakeNotification(new Dictionary<QueueType, double> { [QueueType.Import] = 50.0 }),
                    CancellationToken.None);

                Assert.False(handler.QueueAges.ContainsKey(QueueType.Export), "Export key must not survive after snapshot replacement with Import-only data.");
                Assert.True(handler.QueueAges.ContainsKey(QueueType.Import));

                // Confirm the gauge only emits Import, not Export.
                var collectedQueueTypes = new List<string>();
                using var listener = new MeterListener();
                listener.InstrumentPublished = (instrument, l) =>
                {
                    if (instrument.Name == "Jobs.OldestQueuedAge" && ReferenceEquals(instrument.Meter.Scope, factory))
                    {
                        l.EnableMeasurementEvents(instrument);
                    }
                };
                listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) =>
                {
                    foreach (var tag in tags)
                    {
                        if (tag.Key == "queue_type")
                        {
                            collectedQueueTypes.Add(tag.Value?.ToString());
                        }
                    }
                });
                listener.Start();
                listener.RecordObservableInstruments();

                Assert.DoesNotContain("Export", collectedQueueTypes);
                Assert.Contains("Import", collectedQueueTypes);
            }
        }
    }
}
