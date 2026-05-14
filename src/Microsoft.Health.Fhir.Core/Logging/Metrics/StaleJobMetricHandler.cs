// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.StaleJob.Messages;

namespace Microsoft.Health.Fhir.Core.Logging.Metrics
{
    public sealed class StaleJobMetricHandler : BaseMeterMetricHandler, INotificationHandler<StaleJobMetricsNotification>
    {
        private readonly ObservableGauge<double> _ageGauge;
        private readonly ObservableGauge<long> _depthGauge;

        private IReadOnlyDictionary<QueueType, double> _queueAges = new Dictionary<QueueType, double>();
        private IReadOnlyDictionary<QueueType, QueueDepth> _queueDepths = new Dictionary<QueueType, QueueDepth>();

        public StaleJobMetricHandler(IMeterFactory meterFactory)
            : base(meterFactory)
        {
            _ageGauge = MetricMeter.CreateObservableGauge(
                "Jobs.OldestQueuedAgeSeconds",
                ObserveAgeValues,
                unit: "s",
                description: "Age in seconds of the oldest queued job per queue type when no jobs are running.");

            _depthGauge = MetricMeter.CreateObservableGauge(
                "Jobs.QueueDepth",
                ObserveDepthValues,
                unit: "{job}",
                description: "Number of active jobs per queue type, separated by state (pending or running).");
        }

        internal IReadOnlyDictionary<QueueType, double> QueueAges => Volatile.Read(ref _queueAges);

        internal IReadOnlyDictionary<QueueType, QueueDepth> QueueDepths => Volatile.Read(ref _queueDepths);

        public Task Handle(StaleJobMetricsNotification notification, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(notification, nameof(notification));

            // Each reference is replaced atomically; both will be consistent within one or two collection cycles.
            Volatile.Write(ref _queueAges, new Dictionary<QueueType, double>(notification.QueueAges));
            Volatile.Write(ref _queueDepths, new Dictionary<QueueType, QueueDepth>(notification.QueueDepths));

            return Task.CompletedTask;
        }

        private IEnumerable<Measurement<double>> ObserveAgeValues()
        {
            var snapshot = Volatile.Read(ref _queueAges);
            return snapshot.Select(kv => new Measurement<double>(
                kv.Value,
                new KeyValuePair<string, object>("queue_type", kv.Key.ToString())));
        }

        private IEnumerable<Measurement<long>> ObserveDepthValues()
        {
            var snapshot = Volatile.Read(ref _queueDepths);
            var measurements = new List<Measurement<long>>(snapshot.Count * 2);
            foreach (var (queueType, depth) in snapshot)
            {
                var queueTag = new KeyValuePair<string, object>("queue_type", queueType.ToString());
                measurements.Add(new Measurement<long>(depth.Pending, queueTag, new KeyValuePair<string, object>("state", "pending")));
                measurements.Add(new Measurement<long>(depth.Running, queueTag, new KeyValuePair<string, object>("state", "running")));
            }

            return measurements;
        }
    }
}
