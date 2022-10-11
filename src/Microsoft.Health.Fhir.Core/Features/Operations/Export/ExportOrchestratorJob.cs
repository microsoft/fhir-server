// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    public class ExportOrchestratorJob : IJob
    {
        private const int DefaultPollingFrequencyInSeconds = 3;

        private JobInfo _jobInfo;
        private IQueueClient _queueClient;
        private ILogger<ExportOrchestratorJob> _logger;

        public ExportOrchestratorJob(
            JobInfo jobInfo,
            IQueueClient queueClient,
            ILoggerFactory loggerFactory)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory));

            _jobInfo = jobInfo;
            _queueClient = queueClient;
            _logger = loggerFactory.CreateLogger<ExportOrchestratorJob>();
        }

        public int PollingFrequencyInSeconds { get; set; } = DefaultPollingFrequencyInSeconds;

        public async Task<string> ExecuteAsync(IProgress<string> progress, CancellationToken cancellationToken)
        {
            // If the filter attribute is null and it isn't patient or group export...
            // Call the SQL stored procedure to get resource surogate ids based on start and end time
            // Make a batch of export processing jobs
            // Repeat if needed
            // else
            // Make one processing job for the entire export

            // Use progress to check the status as one of: New, Processing, Done

            ExportJobRecord record = JsonConvert.DeserializeObject<ExportJobRecord>(_jobInfo.Definition);

            if (_jobInfo.Result.Equals(ExportOrchestratorJobProgress.Initialized.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                var processingRecord = new ExportJobRecord(
                    record.RequestUri,
                    record.ExportType,
                    record.ExportFormat,
                    record.ResourceType,
                    record.Filters,
                    record.Hash,
                    record.RollingFileSizeInMB,
                    record.RequestorClaims,
                    record.Since,
                    record.Till,
                    record.GroupId,
                    record.StorageAccountConnectionHash,
                    record.StorageAccountUri,
                    record.AnonymizationConfigurationCollectionReference,
                    record.AnonymizationConfigurationLocation,
                    record.AnonymizationConfigurationFileETag,
                    record.MaximumNumberOfResourcesPerQuery,
                    record.NumberOfPagesPerCommit,
                    record.StorageAccountContainerName,
                    record.SchemaVersion,
                    (int)JobType.ExportProcessing);

                string[] definitions = new string[] { JsonConvert.SerializeObject(processingRecord) };

                await _queueClient.EnqueueAsync((byte)QueueType.Export, definitions, _jobInfo.GroupId, false, false, cancellationToken);
                progress.Report(ExportOrchestratorJobProgress.PreprocessCompleted.ToString());
            }

            if (_jobInfo.Result.Equals(ExportOrchestratorJobProgress.PreprocessCompleted.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                bool allJobsComplete;

                do
                {
                    var groupJobs = await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Export, _jobInfo.GroupId, false, cancellationToken);
                    allJobsComplete = true;
                    foreach (var job in groupJobs)
                    {
                        if (job.Id != _jobInfo.Id && (job.Status == JobStatus.Running || job.Status == JobStatus.Created))
                        {
                            allJobsComplete = false;
                            break;
                        }
                    }

                    await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                }
                while (!allJobsComplete);

                progress.Report(ExportOrchestratorJobProgress.SubJobsCompleted.ToString());
            }

            if (_jobInfo.Result.Equals(ExportOrchestratorJobProgress.SubJobsCompleted.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                // get all processing jobs and merge their results into a master export job result.
                var groupJobs = await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Export, _jobInfo.GroupId, false, cancellationToken);
                bool jobFailed = false;
                foreach (var job in groupJobs)
                {
                    if (job.Id != _jobInfo.Id)
                    {
                        var processResult = JsonConvert.DeserializeObject<ExportJobRecord>(job.Result);
                        foreach (var output in processResult.Output)
                        {
                            if (record.Output.TryGetValue(output.Key, out var exportFileInfos))
                            {
                                exportFileInfos.AddRange(output.Value);
                            }
                            else
                            {
                                record.Output.Add(output.Key, output.Value);
                            }
                        }

                        if (processResult.FailureDetails != null)
                        {
                            if (record.FailureDetails != null)
                            {
                                record.FailureDetails = processResult.FailureDetails;
                            }
                            else if (!processResult.FailureDetails.FailureReason.Equals(record.FailureDetails.FailureReason, StringComparison.OrdinalIgnoreCase))
                            {
                                record.FailureDetails = new JobFailureDetails(record.FailureDetails.FailureReason + "\r\n" + processResult.FailureDetails.FailureReason, record.FailureDetails.FailureStatusCode);
                            }

                            jobFailed = true;
                        }
                    }
                }

                if (jobFailed)
                {
                    throw new JobExecutionException(record.FailureDetails.FailureReason);
                }
            }

            return JsonConvert.SerializeObject(record);
        }
    }
}
