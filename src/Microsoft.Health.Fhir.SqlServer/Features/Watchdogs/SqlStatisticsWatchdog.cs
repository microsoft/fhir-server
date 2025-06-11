// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.JobManagement;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.TaskManagement;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    /// <summary>
    /// Watchdog responsible for updating SQL statistics on database tables.
    /// This watchdog runs once per day and only executes if there are no active background jobs.
    /// It also handles JobCompletedNotification events to update statistics after large-scale data changes.
    /// </summary>
    internal sealed class SqlStatisticsWatchdog : Watchdog<SqlStatisticsWatchdog>, INotificationHandler<JobCompletedNotification>
    {
        private readonly SchemaInformation _schemaInformation;
        private readonly IQueueClient _queueClient;
        private readonly ISqlRetryService _sqlRetryService;
        private readonly ILogger<SqlStatisticsWatchdog> _logger;

        public SqlStatisticsWatchdog(
            IQueueClient queueClient,
            ISqlRetryService sqlRetryService,
            SchemaInformation schemaInformation,
            ILogger<SqlStatisticsWatchdog> logger)
            : base(sqlRetryService, logger)
        {
            _schemaInformation = EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            _queueClient = EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            _sqlRetryService = EnsureArg.IsNotNull(sqlRetryService, nameof(sqlRetryService));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        internal SqlStatisticsWatchdog()
            : base()
        {
            // this is used to get param names for testing
        }

        internal string IsEnabledId => $"{Name}.IsEnabled";

        public override double LeasePeriodSec { get; internal set; } = 2 * 3600; // 2 hours

        public override bool AllowRebalance { get; internal set; } = false; // Don't allow rebalancing for simplicity

        public override double PeriodSec { get; internal set; } = 24 * 3600; // 24 hours (once per day)

        /// <summary>
        /// Main execution method that checks for active jobs and updates SQL statistics if none are found.
        /// </summary>
        protected override async Task RunWorkAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("SqlStatisticsWatchdog: starting...");

            if (!await IsEnabledAsync(cancellationToken))
            {
                _logger.LogInformation("SqlStatisticsWatchdog is not enabled. Exiting...");
                return;
            }

            // Check for any active background jobs across all queue types
            var hasActiveJobs = await HasActiveBackgroundJobsAsync(0, cancellationToken);
            if (hasActiveJobs)
            {
                _logger.LogInformation("SqlStatisticsWatchdog: Found active background jobs. Skipping statistics update to avoid interference.");
                return;
            }

            _logger.LogInformation("SqlStatisticsWatchdog: No active background jobs found. Proceeding with statistics update.");

            try
            {
                await UpdateSqlStatisticsAsync(cancellationToken);
                _logger.LogInformation("SqlStatisticsWatchdog: Successfully completed statistics update.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SqlStatisticsWatchdog: Failed to update SQL statistics.");
                await _sqlRetryService.TryLogEvent("SqlStatisticsWatchdog", "Error", ex.ToString(), null, cancellationToken);
                throw;
            }
        }

        /// <summary>
        /// Handles the JobCompletedNotification event for jobs that cause large data changes.
        /// </summary>
        /// <param name="notification">The job completed notification.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task Handle(JobCompletedNotification notification, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(notification, nameof(notification));

            JobInfo jobInfo = notification.JobInfo;
            var jobType = (JobType?)jobInfo.GetJobTypeId();

            // Only process Import and BulkDelete job types as these cause large data changes
            if (jobType != JobType.BulkDeleteOrchestrator && jobType != JobType.ImportOrchestrator)
            {
                _logger.LogDebug("SqlStatisticsWatchdog: Job completion notification received for job {JobId} of type {QueueType}, which is not a job type that causes large data changes. Ignoring.", jobInfo.Id, (QueueType)jobInfo.QueueType);
                return;
            }

            _logger.LogInformation("SqlStatisticsWatchdog: Job completion notification received for job {JobId} of type {QueueType}.", jobInfo.Id, (QueueType)jobInfo.QueueType);

            if (!await IsEnabledAsync(cancellationToken))
            {
                _logger.LogInformation("SqlStatisticsWatchdog is not enabled. Exiting...");
                return;
            }

            // Check if any other jobs are running before updating statistics
            var hasActiveJobs = await HasActiveBackgroundJobsAsync(jobInfo.GroupId, cancellationToken);
            if (hasActiveJobs)
            {
                _logger.LogInformation("SqlStatisticsWatchdog: Other active jobs found. Skipping statistics update to avoid interference.");
                return;
            }

            _logger.LogInformation("SqlStatisticsWatchdog: No other active jobs found. Proceeding with statistics update after job {JobId} completion.", jobInfo.Id);

            try
            {
                await UpdateSqlStatisticsAsync(cancellationToken);
                _logger.LogInformation("SqlStatisticsWatchdog: Successfully completed statistics update after job {JobId} completion.", jobInfo.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SqlStatisticsWatchdog: Failed to update SQL statistics after job {JobId} completion.", jobInfo.Id);
                await _sqlRetryService.TryLogEvent("SqlStatisticsWatchdog", "Error", ex.ToString(), null, cancellationToken);
            }
        }

        /// <summary>
        /// Checks if there are any active background jobs in any queue type.
        /// </summary>
        private async Task<bool> HasActiveBackgroundJobsAsync(long groupId, CancellationToken cancellationToken)
        {
            // Check each queue type for active jobs (Created or Running status)
            var queueTypes = new[]
            {
                QueueType.Import,
                QueueType.BulkDelete,
            };

            foreach (var queueType in queueTypes)
            {
                try
                {
                    // We only need to check if jobs exist, so don't return definitions (false)
                    var jobs = await _queueClient.GetJobByGroupIdAsync((byte)queueType, groupId, false, cancellationToken);

                    // Check for jobs with Created or Running status
                    if (jobs != null)
                    {
                        foreach (var job in jobs)
                        {
                            if (job.Status == JobStatus.Created || job.Status == JobStatus.Running)
                            {
                                _logger.LogInformation($"SqlStatisticsWatchdog: Found active job (ID: {job.Id}, Status: {job.Status}) in queue type {queueType}.");
                                return true;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"SqlStatisticsWatchdog: Error checking for active jobs in queue type {queueType}. Assuming jobs are running to be safe.");
                    return true; // Assume there are active jobs if we can't check properly
                }
            }

            return false;
        }

        /// <summary>
        /// Updates SQL statistics on all user tables in the database.
        /// </summary>
        private async Task UpdateSqlStatisticsAsync(CancellationToken cancellationToken)
        {
            await using var cmd = new SqlCommand("dbo.UpdateSqlStatistics") { CommandType = CommandType.StoredProcedure, CommandTimeout = 0 }; // Long running operation
            await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken);
        }

        /// <summary>
        /// Checks if the watchdog is enabled.
        /// </summary>
        private async Task<bool> IsEnabledAsync(CancellationToken cancellationToken)
        {
            if (_schemaInformation.Current < SchemaVersionConstants.UpdateStatistics)
            {
                return false;
            }

            var value = await GetNumberParameterByIdAsync(IsEnabledId, cancellationToken);
            return value == 1;
        }

        /// <summary>
        /// Initializes additional parameters for the watchdog.
        /// </summary>
        protected override async Task InitAdditionalParamsAsync()
        {
            _logger.LogInformation("SqlStatisticsWatchdog.InitAdditionalParamsAsync starting...");

            await using var cmd = new SqlCommand(@"
INSERT INTO dbo.Parameters (Id,Number) SELECT @IsEnabledId, 1
            ");
            cmd.Parameters.AddWithValue("@IsEnabledId", IsEnabledId);
            await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, CancellationToken.None);

            _logger.LogInformation("SqlStatisticsWatchdog.InitAdditionalParamsAsync completed.");
        }
    }
}
