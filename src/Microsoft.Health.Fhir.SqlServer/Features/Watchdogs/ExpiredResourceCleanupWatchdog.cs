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
        private const int DefaultPeriodSec = 2 * 3600; // 2 hours
        private const int DefaultLeasePeriodSec = 3600; // 1 hour

        private readonly ISqlRetryService _sqlRetryService;
        private readonly IQueueClient _queueClient;
        private readonly ILogger<ExpiredResourceCleanupWatchdog> _logger;
        private readonly ExpiredResourceConfiguration _configuration;

        private int _retentionPeriodDays;

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

        internal string RetentionPeriodDaysId => $"{Name}.RetentionPeriodDays";

        internal string IsEnabledId => $"{Name}.IsEnabled";

        internal string DeleteOperationId => $"{Name}.DeleteOperation";

        /// <inheritdoc/>
        public override double LeasePeriodSec { get; internal set; } = DefaultLeasePeriodSec;

        /// <inheritdoc/>
        public override bool AllowRebalance { get; internal set; } = false;

        /// <inheritdoc/>
        public override double PeriodSec { get; internal set; } = DefaultPeriodSec;

        /// <inheritdoc/>
        protected override async Task RunWorkAsync(CancellationToken cancellationToken)
        {
            if (!_configuration.Enabled)
            {
                _logger.LogDebug("ExpiredResourceCleanupWatchdog is disabled. Skipping cleanup.");
                return;
            }

            // Use configuration values if enabled via config, otherwise fall back to database parameters
            if (_configuration.Enabled)
            {
                _retentionPeriodDays = _configuration.RetentionPeriodDays;
            }

            await EnqueueBulkDeleteJobAsync(cancellationToken);
        }

        /// <inheritdoc/>
        protected override Task InitAdditionalParamsAsync()
        {
            return Task.CompletedTask;
        }

        private async Task EnqueueBulkDeleteJobAsync(CancellationToken cancellationToken)
        {
            try
            {
                var cutoffDate = Clock.UtcNow.AddDays(-_retentionPeriodDays);
                var cutoffDateString = cutoffDate.ToString("yyyy-MM-ddTHH:mm:ssZ");

                var searchParameters = new List<Tuple<string, string>>
                {
                    Tuple.Create("_expiryDate", $"lt{cutoffDateString}"),
                };

                var definition = new BulkDeleteDefinition(
                    JobType.BulkDeleteOrchestrator,
                    DeleteOperation.HardDelete,
                    type: null, // null type means all resource types
                    searchParameters,
                    excludedResourceTypes: null,
                    url: $"ExpiredResourceCleanupWatchdog?_expiryDate=lt{cutoffDateString}",
                    baseUrl: string.Empty,
                    parentRequestId: Guid.NewGuid().ToString(),
                    versionType: ResourceVersionType.Latest,
                    removeReferences: false);

                var jobs = await _queueClient.EnqueueAsync(QueueType.BulkDelete, cancellationToken, definitions: definition);

                if (jobs != null && jobs.Count > 0)
                {
                    _logger.LogInformation(
                        "ExpiredResourceCleanupWatchdog: Enqueued bulk delete job {JobId} to delete resources older than {CutoffDate} (retention period: {RetentionPeriodDays} days).",
                        jobs[0].Id,
                        cutoffDateString,
                        _retentionPeriodDays);
                }
                else
                {
                    _logger.LogWarning("ExpiredResourceCleanupWatchdog: Failed to enqueue bulk delete job.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ExpiredResourceCleanupWatchdog: Error while enqueuing bulk delete job.");
            }
        }
    }
}
