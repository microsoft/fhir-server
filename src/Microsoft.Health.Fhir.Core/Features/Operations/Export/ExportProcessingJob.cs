﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    [JobTypeId((int)JobType.ExportProcessing)]
    public class ExportProcessingJob : IJob
    {
        private readonly Func<IExportJobTask> _exportJobTaskFactory;
        private readonly IQueueClient _queueClient;
        private readonly ILogger<ExportProcessingJob> _logger;

        public ExportProcessingJob(
            Func<IExportJobTask> exportJobTaskFactory,
            IQueueClient queueClient,
            ILogger<ExportProcessingJob> logger)
        {
            _exportJobTaskFactory = EnsureArg.IsNotNull(exportJobTaskFactory, nameof(exportJobTaskFactory));
            _queueClient = EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        public Task<string> ExecuteAsync(JobInfo jobInfo, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));

            ExportJobRecord record = JsonConvert.DeserializeObject<ExportJobRecord>(string.IsNullOrEmpty(jobInfo.Result) ? jobInfo.Definition : jobInfo.Result);
            record.Id = jobInfo.Id.ToString();
            IExportJobTask exportJobTask = _exportJobTaskFactory();

            // The ExportJobTask was used to handling the database updates and etags itself, but the new job hosting flow manages it in a central location.
            // This method allows the same class to be used in both Cosmos (with the old method) and SQL (with the new method).
            // The etag passed to the ExportJobTask is unused, the actual etag is managed in the JobHosting class.
            exportJobTask.UpdateExportJob = UpdateExportJob;
            Task exportTask = exportJobTask.ExecuteAsync(record, WeakETag.FromVersionId("0"), cancellationToken);
            return exportTask.ContinueWith(
                (Task parent) =>
                {
                    switch (record.Status)
                    {
                        case OperationStatus.Completed:
                            record.Id = string.Empty;
                            record.StartTime = null;
                            record.EndTime = null;
                            if (record.Output != null)
                            {
                                jobInfo.Data = record.Output.Values.Sum(infos => infos.Sum(info => info.Count));
                            }

                            return JsonConvert.SerializeObject(record);
                        case OperationStatus.Failed:
                            throw new JobExecutionException(record.FailureDetails.FailureReason, record);
                        case OperationStatus.Canceled:
                            throw new OperationCanceledException($"[GroupId:{jobInfo.GroupId}/JobId:{jobInfo.Id}] Export job cancelled.");
                        case OperationStatus.Queued:
                        case OperationStatus.Running:
                            // If code works as designed, this exception shouldn't be reached
                            throw new JobExecutionException($"[GroupId:{jobInfo.GroupId}/JobId:{jobInfo.Id}] Export job finished in non-terminal state. See logs from ExportJobTask.", record);
                        default:
                            // If code works as designed, this exception shouldn't be reached
                            throw new JobExecutionException($"[GroupId:{jobInfo.GroupId}/JobId:{jobInfo.Id}] Job status not set.");
                    }
                },
                cancellationToken,
                TaskContinuationOptions.None,
                TaskScheduler.Current);

            async Task<ExportJobOutcome> UpdateExportJob(ExportJobRecord exportJobRecord, WeakETag weakETag, CancellationToken innerCancellationToken)
            {
                record = exportJobRecord;

                if (record.Status == OperationStatus.Running)
                {
                    // Update Export Job is called in three scenarios:
                    // 1. A page of search results is processed and there is a continuation token.
                    // 2. A filter has finished being used by a search
                    // 3. The export job has reached a terminal state
                    // This section is only reached in cases 1 & 2.
                    // For jobs running in parallel such that each job only processes one page of results this section will never be reached as there are no filters and no continuation tokens.
                    // For single threaded jobs this section will complete a job after every page of results is processed and make a new job for the next page.
                    // This will allow for saving intermediate states of the job.

                    var existingJobs = await _queueClient.GetJobByGroupIdAsync(QueueType.Export, jobInfo.GroupId, false, innerCancellationToken);

                    // Only queue new jobs if group has no canceled jobs. This ensures canceled jobs won't continue to queue new jobs (Cosmos cancel is not 100% reliable).
                    if (!existingJobs.Any(job => job.Status == JobStatus.Cancelled || job.CancelRequested))
                    {
                        var definition = jobInfo.DeserializeDefinition<ExportJobRecord>();

                        // Checks that a follow up job has not already been made. Extra checks are needed for parallel jobs by parallelization factors.
                        var newerJobs = existingJobs.Where(existingJob => existingJob.Definition is not null).Where(existingJob =>
                        {
                            var existingDefinition = existingJob.DeserializeDefinition<ExportJobRecord>();
                            return existingJob.Id > jobInfo.Id && existingDefinition.ResourceType == definition.ResourceType && existingDefinition.FeedRange == definition.FeedRange;
                        });

                        if (!newerJobs.Any())
                        {
                            definition.Progress = exportJobRecord.Progress;
                            var newJob = await _queueClient.EnqueueAsync(QueueType.Export, innerCancellationToken, jobInfo.GroupId, definitions: definition);
                            _logger.LogJobInformation(jobInfo, $"ExportProcessingJob segment continuation. New job(s): {string.Join(',', newJob.Select(x => x.Id))}");
                        }
                        else
                        {
                            _logger.LogWarning($"[GroupId:{jobInfo.GroupId}/JobId:{jobInfo.Id}] ExportProcessingJob segment continuation error. Unexpected newer job(s) exists. JobIds: {string.Join(',', newerJobs.Select(x => x.Id))}.");
                        }
                    }

                    record.Status = OperationStatus.Completed;

                    // TODO: Ideally we would process predefined pages of data like SQL vs pagination through continuation tokens/this exception.
                    throw new JobSegmentCompletedException();
                }

                return new ExportJobOutcome(exportJobRecord, weakETag);
            }
        }
    }
}
