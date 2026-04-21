// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
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

        internal readonly ConcurrentDictionary<QueueType, double> QueueAges = new();

        public StaleJobMetricHandler(IMeterFactory meterFactory)
            : base(meterFactory)
        {
            _gauge = MetricMeter.CreateObservableGauge(
                "Jobs.OldestQueuedAgeSeconds",
                ObserveValues,
                unit: "s",
                description: "Age in seconds of the oldest queued job per queue type when no jobs are running.");
        }

        public Task Handle(StaleJobMetricsNotification notification, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(notification, nameof(notification));

            foreach (var (queueType, age) in notification.QueueAges)
            {
                QueueAges[queueType] = age;
            }

            return Task.CompletedTask;
        }

        private IEnumerable<Measurement<double>> ObserveValues()
        {
            return QueueAges.Select(kv => new Measurement<double>(
                kv.Value,
                new KeyValuePair<string, object?>("queue_type", kv.Key.ToString())));
        }
    }
}
