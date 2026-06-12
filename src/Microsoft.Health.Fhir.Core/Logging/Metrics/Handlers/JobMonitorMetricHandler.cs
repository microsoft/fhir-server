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
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.JobMonitor.Messages;

namespace Microsoft.Health.Fhir.Core.Logging.Metrics.Handlers
{
    public sealed class JobMonitorMetricHandler : BaseMeterMetricHandler, INotificationHandler<JobMonitorMetricsNotification>
    {
        /// <summary>
        /// Maximum age of a published snapshot before gauge callbacks suppress all measurements.
        /// Set to 5× the 60-second publish period so a single missed tick does not silence the gauges.
        /// </summary>
        internal const int SnapshotStaleCutoffSeconds = 300;

        private readonly ObservableGauge<double> _ageGauge;
        private readonly ObservableGauge<long> _depthGauge;

        private Snapshot _snapshot = new Snapshot(
            new Dictionary<QueueType, double>(),
            new Dictionary<QueueType, QueueDepth>(),
            DateTimeOffset.MinValue);

        public JobMonitorMetricHandler(IMeterFactory meterFactory)
            : base(meterFactory)
        {
            _ageGauge = MetricMeter.CreateObservableGauge(
                "Jobs.OldestQueuedAge",
                ObserveAgeValues,
                unit: "s",
                description: "Age in seconds of the oldest pending (Created) job per queue type.");

            _depthGauge = MetricMeter.CreateObservableGauge(
                "Jobs.QueueDepth",
                ObserveDepthValues,
                unit: "{job}",
                description: "Number of active jobs per queue type, separated by state (pending or running).");
        }

        internal IReadOnlyDictionary<QueueType, double> QueueAges => Volatile.Read(ref _snapshot).Ages;

        internal IReadOnlyDictionary<QueueType, QueueDepth> QueueDepths => Volatile.Read(ref _snapshot).Depths;

        public Task Handle(JobMonitorMetricsNotification notification, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(notification, nameof(notification));

            Volatile.Write(
                ref _snapshot,
                new Snapshot(
                    new Dictionary<QueueType, double>(notification.QueueAges),
                    new Dictionary<QueueType, QueueDepth>(notification.QueueDepths),
                    ClockResolver.TimeProvider.GetUtcNow()));

            return Task.CompletedTask;
        }

        private IEnumerable<Measurement<double>> ObserveAgeValues()
        {
            var snapshot = Volatile.Read(ref _snapshot);
            if (IsStale(snapshot))
            {
                return Array.Empty<Measurement<double>>();
            }

            return snapshot.Ages.Select(kv => new Measurement<double>(
                kv.Value,
                new KeyValuePair<string, object>("queue_type", kv.Key.ToString())));
        }

        private IEnumerable<Measurement<long>> ObserveDepthValues()
        {
            var snapshot = Volatile.Read(ref _snapshot);
            if (IsStale(snapshot))
            {
                return Array.Empty<Measurement<long>>();
            }

            var measurements = new List<Measurement<long>>(snapshot.Depths.Count * 2);
            foreach (var (queueType, depth) in snapshot.Depths)
            {
                var queueTag = new KeyValuePair<string, object>("queue_type", queueType.ToString());
                measurements.Add(new Measurement<long>(depth.Pending, queueTag, new KeyValuePair<string, object>("state", "pending")));
                measurements.Add(new Measurement<long>(depth.Running, queueTag, new KeyValuePair<string, object>("state", "running")));
            }

            return measurements;
        }

        private static bool IsStale(Snapshot snapshot)
        {
            return (ClockResolver.TimeProvider.GetUtcNow() - snapshot.Timestamp).TotalSeconds > SnapshotStaleCutoffSeconds;
        }

        private sealed record Snapshot(
            IReadOnlyDictionary<QueueType, double> Ages,
            IReadOnlyDictionary<QueueType, QueueDepth> Depths,
            DateTimeOffset Timestamp);
    }
}
