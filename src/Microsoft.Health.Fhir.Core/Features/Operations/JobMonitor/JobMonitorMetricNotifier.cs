// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Operations.JobMonitor.Messages;
using Microsoft.Health.Fhir.Core.Logging.Metrics;

namespace Microsoft.Health.Fhir.Core.Features.Operations.JobMonitor
{
    public sealed class JobMonitorMetricNotifier : INotificationHandler<JobMonitorMetricsNotification>
    {
        /// <summary>
        /// Maximum age of a published snapshot before gauge callbacks suppress all measurements.
        /// Set to 5× the 60-second publish period so a single missed tick does not silence the gauges.
        /// </summary>
        internal const int SnapshotStaleCutoffSeconds = 300;

        private readonly IJobMonitorMetricHandler _metricHandler;

        private Snapshot _snapshot;

        public JobMonitorMetricNotifier(IJobMonitorMetricHandler metricHandler)
        {
            _metricHandler = EnsureArg.IsNotNull(metricHandler, nameof(metricHandler));

            _snapshot = new Snapshot(new Dictionary<QueueType, long>(), new Dictionary<QueueType, QueueDepth>());
        }

        internal IReadOnlyDictionary<QueueType, long> QueueAges => Volatile.Read(ref _snapshot).Ages;

        internal IReadOnlyDictionary<QueueType, QueueDepth> QueueDepths => Volatile.Read(ref _snapshot).Depths;

        public Task Handle(JobMonitorMetricsNotification notification, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(notification, nameof(notification));

            foreach (KeyValuePair<QueueType, QueueDepth> item in notification.QueueDepths)
            {
                _metricHandler.ReportJobQueueRunning(item.Key.ToString(), item.Value.Running);
                _metricHandler.ReportJobQueuePending(item.Key.ToString(), item.Value.Pending);
            }

            foreach (KeyValuePair<QueueType, long> item in notification.QueueAges)
            {
                _metricHandler.ReportJobQueueAge(item.Key.ToString(), item.Value);
            }

            Volatile.Write(
                ref _snapshot,
                new Snapshot(
                    new Dictionary<QueueType, long>(notification.QueueAges),
                    new Dictionary<QueueType, QueueDepth>(notification.QueueDepths)));

            return Task.CompletedTask;
        }

        private sealed record Snapshot(IReadOnlyDictionary<QueueType, long> Ages, IReadOnlyDictionary<QueueType, QueueDepth> Depths);
    }
}
