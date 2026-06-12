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
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.JobMonitor.Messages;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    internal sealed class JobMonitorWatchdog : Watchdog<JobMonitorWatchdog>
    {
        /// <summary>
        /// Queue age (in seconds) at or above which a queue is considered stale and logged as a warning.
        /// Below this threshold the age is logged at debug level only. Matches the alerting guidance in
        /// docs/arch/adr-2605-stale-job-monitor.md.
        /// </summary>
        internal const int StaleQueueWarningThresholdSeconds = 600;

        private readonly ISqlRetryService _sqlRetryService;
        private readonly IMediator _mediator;
        private readonly ILogger<JobMonitorWatchdog> _logger;

        public JobMonitorWatchdog(
            ISqlRetryService sqlRetryService,
            ILogger<JobMonitorWatchdog> logger,
            IMediator mediator)
            : base(sqlRetryService, logger)
        {
            _sqlRetryService = EnsureArg.IsNotNull(sqlRetryService, nameof(sqlRetryService));
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

                var utcNow = DateTime.UtcNow;
                var ages = ComputeQueueAges(aggregates, utcNow);
                var depths = ComputeQueueDepths(aggregates);

                foreach (var (queueType, age) in ages.Where(kv => kv.Value > 0))
                {
                    var hasNoRunning = !depths.TryGetValue(queueType, out var depth) || depth.Running == 0;
                    if (age >= StaleQueueWarningThresholdSeconds && hasNoRunning)
                    {
                        _logger.LogWarning(
                            "Stale job queue detected. QueueType={QueueType} OldestJobAgeSecs={Age}",
                            queueType,
                            age);
                    }
                    else
                    {
                        _logger.LogDebug(
                            "Job queue has pending jobs but is not stalled. QueueType={QueueType} OldestJobAgeSecs={Age}",
                            queueType,
                            age);
                    }
                }

                await _mediator.Publish(new JobMonitorMetricsNotification(ages, depths), cancellationToken);
            }
            catch (Exception ex)
            {
                // Rethrow so the FhirTimer base loop keeps running on the next tick but no partial metric
                // data is published this cycle (all-or-nothing publish, per ADR 2605).
                _logger.LogError(ex, "JobMonitorWatchdog: queue metrics were not refreshed this cycle.");
                throw;
            }
        }

        private async Task<IReadOnlyList<QueueStatusAggregate>> GetQueueStatusAggregatesAsync(CancellationToken cancellationToken)
        {
            // Lightweight aggregate over dbo.JobQueue. Only Status 0 (Created) and 1 (Running) are read, and
            // only QueueType, Status, the oldest CreateDate, and a count are returned. The heavy varchar(max)
            // Definition/Result payloads are never touched.
            await using var cmd = new SqlCommand(
                @"SELECT QueueType, Status, MIN(CreateDate) AS OldestCreateDate, COUNT(*) AS JobCount
                  FROM dbo.JobQueue
                  WHERE Status IN (@Created, @Running)
                  GROUP BY QueueType, Status");
            cmd.Parameters.AddWithValue("@Created", (byte)JobStatus.Created);
            cmd.Parameters.AddWithValue("@Running", (byte)JobStatus.Running);

            return await _sqlRetryService.ExecuteReaderAsync(
                cmd,
                reader => new QueueStatusAggregate(
                    (QueueType)reader.GetByte(0),
                    (JobStatus)reader.GetByte(1),
                    reader.GetDateTime(2),
                    reader.GetInt32(3)),
                _logger,
                "Failed to read job queue status aggregates",
                cancellationToken,
                isReadOnly: true);
        }

        internal static Dictionary<QueueType, double> ComputeQueueAges(
            IReadOnlyList<QueueStatusAggregate> aggregates,
            DateTime utcNow)
        {
            // Seed every actionable queue type so empty queues are still reported (age 0).
            var result = MonitoredQueueTypes.ToDictionary(q => q, _ => 0d);

            foreach (var queueType in MonitoredQueueTypes)
            {
                var created = aggregates.FirstOrDefault(a => a.QueueType == queueType && a.Status == JobStatus.Created && a.Count > 0);
                if (created.Count > 0)
                {
                    // Clamp to 0 to absorb clock skew between SQL-stamped CreateDate and app-server utcNow.
                    result[queueType] = Math.Max(0, (utcNow - created.OldestCreateDate).TotalSeconds);
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
        /// A single GROUP BY row over dbo.JobQueue: the count and oldest CreateDate of active jobs of one
        /// <see cref="JobStatus"/> within one <see cref="QueueType"/>.
        /// </summary>
        internal readonly record struct QueueStatusAggregate(
            QueueType QueueType,
            JobStatus Status,
            DateTime OldestCreateDate,
            int Count);
    }
}
