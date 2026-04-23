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
        private readonly ObservableGauge<double> _gauge;

        private IReadOnlyDictionary<QueueType, double> _queueAges = new Dictionary<QueueType, double>();

        public StaleJobMetricHandler(IMeterFactory meterFactory)
            : base(meterFactory)
        {
            _gauge = MetricMeter.CreateObservableGauge(
                "Jobs.OldestQueuedAgeSeconds",
                ObserveValues,
                unit: "s",
                description: "Age in seconds of the oldest queued job per queue type when no jobs are running.");
        }

        internal IReadOnlyDictionary<QueueType, double> QueueAges => Volatile.Read(ref _queueAges);

        public Task Handle(StaleJobMetricsNotification notification, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(notification, nameof(notification));

            // Atomically swap the reference so observers never see a partial update.
            var snapshot = new Dictionary<QueueType, double>(notification.QueueAges);
            Volatile.Write(ref _queueAges, snapshot);

            return Task.CompletedTask;
        }

        private IEnumerable<Measurement<double>> ObserveValues()
        {
            var snapshot = Volatile.Read(ref _queueAges);
            return snapshot.Select(kv => new Measurement<double>(
                kv.Value,
                new KeyValuePair<string, object?>("queue_type", kv.Key.ToString())));
        }
    }
}
