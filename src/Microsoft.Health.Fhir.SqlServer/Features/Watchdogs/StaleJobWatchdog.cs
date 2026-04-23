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
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.StaleJob.Messages;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    internal sealed class StaleJobWatchdog : Watchdog<StaleJobWatchdog>
    {
        private readonly SqlQueueClient _queueClient;
        private readonly IMediator _mediator;
        private readonly ILogger<StaleJobWatchdog> _logger;

        public StaleJobWatchdog(
            ISqlRetryService sqlRetryService,
            ILogger<StaleJobWatchdog> logger,
            SqlQueueClient queueClient,
            IMediator mediator)
            : base(sqlRetryService, logger)
        {
            _queueClient = EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            _mediator = EnsureArg.IsNotNull(mediator, nameof(mediator));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        internal StaleJobWatchdog()
        {
            // used to get param names for testing
        }

        public override double LeasePeriodSec { get; internal set; } = 300;

        public override bool AllowRebalance { get; internal set; } = true;

        public override double PeriodSec { get; internal set; } = 60;

        protected override async Task RunWorkAsync(CancellationToken cancellationToken)
        {
            var jobsByQueue = new Dictionary<QueueType, IReadOnlyList<JobInfo>>();

            foreach (var queueType in Enum.GetValues<QueueType>().Where(q => q != QueueType.Unknown))
            {
                var jobs = await _queueClient.GetActiveJobsByQueueTypeAsync((byte)queueType, false, cancellationToken);
                jobsByQueue[queueType] = jobs;
            }

            var ages = ComputeQueueAges(jobsByQueue, DateTime.UtcNow);

            foreach (var (queueType, age) in ages.Where(kv => kv.Value > 0))
            {
                _logger.LogWarning(
                    "Stale job queue detected. QueueType={QueueType} OldestJobAgeSecs={Age}",
                    queueType,
                    age);
            }

            await _mediator.Publish(new StaleJobMetricsNotification(ages), cancellationToken);
        }

        internal static Dictionary<QueueType, double> ComputeQueueAges(
            Dictionary<QueueType, IReadOnlyList<JobInfo>> jobsByQueue,
            DateTime utcNow)
        {
            var result = new Dictionary<QueueType, double>();

            foreach (var (queueType, jobs) in jobsByQueue)
            {
                if (jobs.Any(j => j.Status == JobStatus.Running))
                {
                    result[queueType] = 0;
                    continue;
                }

                var createdJobs = jobs.Where(j => j.Status == JobStatus.Created).ToList();
                result[queueType] = createdJobs.Count > 0
                    ? (utcNow - createdJobs.Min(j => j.CreateDate)).TotalSeconds
                    : 0;
            }

            return result;
        }
    }
}
