// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    /// <summary>
    /// A watchdog service that periodically enqueues bulk delete jobs to clean up resources
    /// that have not been updated within a configurable retention period.
    /// </summary>
    internal sealed class ExpiredResourceCleanupWatchdog : Watchdog<ExpiredResourceCleanupWatchdog>
    {
        private const int DefaultPeriodSec = 15 * 60; // 15 minutes
        private const int DefaultLeasePeriodSec = 15 * 60; // 15 minutes

        private readonly ISqlRetryService _sqlRetryService;
        private readonly IQueueClient _queueClient;
        private readonly ILogger<ExpiredResourceCleanupWatchdog> _logger;
        private readonly ExpiredResourceConfiguration _configuration;

        public ExpiredResourceCleanupWatchdog(
            ISqlRetryService sqlRetryService,
            IQueueClient queueClient,
            IOptions<WatchdogConfiguration> watchdogConfiguration,
            ILogger<ExpiredResourceCleanupWatchdog> logger)
            : base(sqlRetryService, logger)
        {
            _sqlRetryService = EnsureArg.IsNotNull(sqlRetryService, nameof(sqlRetryService));
            _queueClient = EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            _configuration = EnsureArg.IsNotNull(watchdogConfiguration?.Value?.ExpiredResource, nameof(watchdogConfiguration));
        }

        internal ExpiredResourceCleanupWatchdog()
            : base()
        {
            // This is used to get param names for testing.
        }

        /// <inheritdoc/>
        public override double LeasePeriodSec { get; internal set; } = DefaultLeasePeriodSec;

        /// <inheritdoc/>
        public override bool AllowRebalance { get; internal set; } = false;

        /// <inheritdoc/>
        public override double PeriodSec { get; internal set; } = DefaultPeriodSec;

        /// <summary>
        /// Exposes RunWorkAsync for unit testing purposes.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        internal Task RunWorkForTestingAsync(CancellationToken cancellationToken) => RunWorkAsync(cancellationToken);

        /// <inheritdoc/>
        protected override async Task RunWorkAsync(CancellationToken cancellationToken)
        {
            if (!_configuration.Enabled)
            {
                _logger.LogInformation("ExpiredResourceCleanupWatchdog is disabled. Skipping cleanup.");
                return;
            }

            await EnqueueBulkDeleteJobAsync(cancellationToken);
        }

        private async Task EnqueueBulkDeleteJobAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (await ShouldCreateNewCleanupJob(cancellationToken))
                {
                    var cutoffDate = Clock.UtcNow;
                    var cutoffDateString = cutoffDate.ToString("yyyy-MM-ddTHH:mm:ssZ");

                    var searchParameters = new List<Tuple<string, string>>
                    {
                        Tuple.Create("_expiryDate", $"lt{cutoffDateString}"),
                        Tuple.Create(KnownQueryParameterNames.RemoveReferences, "true"),
                    };

                    var definition = new BulkDeleteDefinition(
                        JobType.BulkDeleteOrchestrator,
                        DeleteOperation.HardDelete,
                        type: null,
                        searchParameters,
                        excludedResourceTypes: null,
                        url: $"./ExpiredResourceCleanupWatchdog",
                        baseUrl: $"./ExpiredResourceCleanupWatchdog",
                        parentRequestId: Guid.NewGuid().ToString(),
                        versionType: ResourceVersionType.Latest,
                        removeReferences: false);

                    var jobs = await _queueClient.EnqueueAsync(QueueType.BulkDelete, cancellationToken, definitions: definition);

                    if (jobs != null && jobs.Count > 0)
                    {
                        _logger.LogInformation(
                            "ExpiredResourceCleanupWatchdog: Enqueued bulk delete job {JobId} to delete resources older than {CutoffDate}.",
                            jobs[0].Id,
                            cutoffDateString);
                    }
                    else
                    {
                        _logger.LogWarning("ExpiredResourceCleanupWatchdog: Failed to enqueue bulk delete job.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ExpiredResourceCleanupWatchdog: Error while enqueuing bulk delete job.");
            }
        }

        private async Task<bool> ShouldCreateNewCleanupJob(CancellationToken cancellationToken)
        {
            // Checks the database for the last bulk delete job definition that was created by this watchdog. If the last job was created before the retention period, returns true to indicate a new job should be created.
            IReadOnlyList<JobInfo> jobs = await _queueClient.GetJobsByQueueTypeAsync((byte)QueueType.BulkDelete, true, cancellationToken, Clock.UtcNow.AddMinutes(-_configuration.ExecutionIntervalInMinutes));

            foreach (var job in jobs)
            {
                var bulkDeleteDefinition = job.DeserializeDefinition<BulkDeleteDefinition>();
                if (bulkDeleteDefinition != null && bulkDeleteDefinition.Url == "./ExpiredResourceCleanupWatchdog")
                {
                    _logger.LogInformation(
                        "ExpiredResourceCleanupWatchdog: Last cleanup job {JobId} was created on {CreatedOn}, which is within the retention period. Skipping new job creation.",
                        job.Id,
                        job.CreateDate);
                    return false;
                }
            }

            return true;
        }
    }
}
