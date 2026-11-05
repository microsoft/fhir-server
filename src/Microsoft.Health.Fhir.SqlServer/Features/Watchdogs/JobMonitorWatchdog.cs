// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Medino;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.JobMonitor.Messages;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    internal sealed class JobMonitorWatchdog : Watchdog<JobMonitorWatchdog>
    {
        /// <summary>
        /// Queue age (in seconds) at or above which a queue with zero Running jobs is considered stale
        /// and logged as a warning. Queues below the threshold, or with running jobs, are logged at debug
        /// level only. Matches the alerting guidance in docs/arch/adr-2605-stale-job-monitor.md.
        /// </summary>
        internal const int StaleQueueWarningThresholdSeconds = 600;

        private readonly SqlQueueClient _sqlQueueClient;
        private readonly IMediator _mediator;
        private readonly ILogger<JobMonitorWatchdog> _logger;

        public JobMonitorWatchdog(
            ISqlRetryService sqlRetryService,
            ILogger<JobMonitorWatchdog> logger,
            IMediator mediator,
            SqlQueueClient sqlQueueClient)
            : base(sqlRetryService, logger)
        {
            _sqlQueueClient = EnsureArg.IsNotNull(sqlQueueClient, nameof(sqlQueueClient));
            _mediator = EnsureArg.IsNotNull(mediator, nameof(mediator));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        internal JobMonitorWatchdog()
        {
            // used to get param names for testing
        }

        public override double LeasePeriodSec { get; internal set; } = 300;

        public override bool AllowRebalance { get; internal set; } = true;

        public override double PeriodSec { get; internal set; } = 60;

        private static IEnumerable<QueueType> MonitoredQueueTypes =>
            Enum.GetValues<QueueType>().Where(q => q != QueueType.Unknown);

        protected override async Task RunWorkAsync(CancellationToken cancellationToken)
        {
            try
            {
                IReadOnlyList<QueueStatusAggregate> aggregates = await GetQueueStatusAggregatesAsync(cancellationToken);

                var utcNow = ClockResolver.TimeProvider.GetUtcNow().UtcDateTime;
                var ages = ComputeQueueAges(aggregates, utcNow);
                var depths = ComputeQueueDepths(aggregates);

                foreach (var (queueType, age) in ages.Where(kv => kv.Value > 0))
                {
                    var hasNoRunning = !depths.TryGetValue(queueType, out var depth) || depth.Running == 0;
                    if (age >= StaleQueueWarningThresholdSeconds && hasNoRunning)
                    {
                        _logger.LogWarning(
                            "Stale job queue detected. QueueType={QueueType} OldestJobAgeSecs={OldestJobAgeSecs}",
                            queueType,
                            age);
                    }
                    else
                    {
                        _logger.LogDebug(
                            "Job queue has pending jobs but is not stalled. QueueType={QueueType} OldestJobAgeSecs={OldestJobAgeSecs}",
                            queueType,
                            age);
                    }
                }

                await _mediator.PublishAsync(new JobMonitorMetricsNotification(ages, depths), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Shutdown or sibling-watchdog cancellation, not a monitor failure; FhirTimer handles it.
                throw;
            }
            catch (Exception ex)
            {
                // Publish is the last statement of the try, so a failure publishes nothing this cycle
                // (all-or-nothing, per ADR 2605). Rethrow so FhirTimer records the failing state.
                _logger.LogError(ex, "JobMonitorWatchdog: queue metrics were not refreshed this cycle.");
                throw;
            }
        }

        internal async Task<IReadOnlyList<QueueStatusAggregate>> GetQueueStatusAggregatesAsync(CancellationToken cancellationToken)
        {
            var aggregates = new List<QueueStatusAggregate>();
            foreach (var queueType in MonitoredQueueTypes)
            {
                // TODO: Pass ReturnDefinition = false through the queue client after schema 122 is deployed.
                var jobs = await _sqlQueueClient.GetActiveJobsByQueueTypeAsync((byte)queueType, returnParentOnly: false, cancellationToken);
                foreach (var status in new[] { JobStatus.Created, JobStatus.Running })
                {
                    // A job can finish between GetActiveJobs selecting its ID and GetJobs reading it.
                    var matchingJobs = jobs.Where(job => job.Status == status).ToList();
                    if (matchingJobs.Count > 0)
                    {
                        aggregates.Add(new QueueStatusAggregate(
                            queueType,
                            status,
                            matchingJobs.Min(job => job.CreateDate),
                            matchingJobs.Count));
                    }
                }
            }

            return aggregates;
        }

        internal static Dictionary<QueueType, long> ComputeQueueAges(
            IReadOnlyList<QueueStatusAggregate> aggregates,
            DateTime utcNow)
        {
            // Seed every actionable queue type so empty queues are still reported (age 0).
            var result = MonitoredQueueTypes.ToDictionary(q => q, _ => 0L);

            foreach (var queueType in MonitoredQueueTypes)
            {
                var created = aggregates.FirstOrDefault(a => a.QueueType == queueType && a.Status == JobStatus.Created && a.Count > 0);
                if (created.Count > 0)
                {
                    // Clamp to 0 to absorb clock skew between SQL-stamped CreateDate and app-server utcNow.
                    result[queueType] = Math.Max(0L, (long)(utcNow - created.OldestCreateDate).TotalSeconds);
                }
            }

            return result;
        }

        internal static Dictionary<QueueType, QueueDepth> ComputeQueueDepths(
            IReadOnlyList<QueueStatusAggregate> aggregates)
        {
            var result = new Dictionary<QueueType, QueueDepth>();

            foreach (var queueType in MonitoredQueueTypes)
            {
                int pending = aggregates
                    .Where(a => a.QueueType == queueType && a.Status == JobStatus.Created)
                    .Sum(a => a.Count);
                int running = aggregates
                    .Where(a => a.QueueType == queueType && a.Status == JobStatus.Running)
                    .Sum(a => a.Count);
                result[queueType] = new QueueDepth(pending, running);
            }

            return result;
        }

        /// <summary>
        /// The count and oldest CreateDate of active jobs of one
        /// <see cref="JobStatus"/> within one <see cref="QueueType"/>.
        /// </summary>
        internal readonly record struct QueueStatusAggregate(
            QueueType QueueType,
            JobStatus Status,
            DateTime OldestCreateDate,
            int Count);
    }
}
