// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;
using Polly;
using Polly.Retry;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    [JobTypeId((int)JobType.ExportProcessing)]
    public class ExportProcessingJob : IJob
    {
        private readonly Func<IExportJobTask> _exportJobTaskFactory;
        private readonly Func<IScoped<ISearchService>> _searchServiceFactory;
        private readonly IQueueClient _queueClient;
        private readonly ILogger<ExportProcessingJob> _logger;

        private const int OomReductionFactor = 10;
        private const int MinEffectiveBatchSize = 1;
        private const int MaxOomReductionsBeforeSoftFail = 3;

        /// <summary>
        /// Current effective batch size for fetching resources. Starts at the configured MaximumNumberOfResourcesPerQuery
        /// but may be reduced if OutOfMemoryException is encountered during processing.
        /// </summary>
        private int _effectiveBatchSize;

        public ExportProcessingJob(
            Func<IExportJobTask> exportJobTaskFactory,
            Func<IScoped<ISearchService>> searchServiceFactory,
            IQueueClient queueClient,
            ILogger<ExportProcessingJob> logger)
        {
            _exportJobTaskFactory = EnsureArg.IsNotNull(exportJobTaskFactory, nameof(exportJobTaskFactory));
            _searchServiceFactory = EnsureArg.IsNotNull(searchServiceFactory, nameof(searchServiceFactory));
            _queueClient = EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        public async Task<string> ExecuteAsync(JobInfo jobInfo, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));

            ExportJobRecord record = JsonConvert.DeserializeObject<ExportJobRecord>(string.IsNullOrEmpty(jobInfo.Result) ? jobInfo.Definition : jobInfo.Result);
            record.Id = jobInfo.Id.ToString();

            _effectiveBatchSize = (int)record.MaximumNumberOfResourcesPerQuery;

            bool isSqlPath = !string.IsNullOrEmpty(record.StartSurrogateId) && !string.IsNullOrEmpty(record.EndSurrogateId);

            if (isSqlPath)
            {
                await ExecuteWithSurrogateIdRangeSplittingAsync(jobInfo, record, cancellationToken);
            }
            else
            {
                await ExecuteWithBatchReductionAsync(jobInfo, record, cancellationToken);
            }

            return ProcessResult(jobInfo, record);
        }

        /// <summary>
        /// SQL path: On OOM, split the surrogate ID range into smaller sub-ranges using GetSurrogateIdRanges
        /// and process each sub-range independently
        /// </summary>
        private async Task ExecuteWithSurrogateIdRangeSplittingAsync(JobInfo jobInfo, ExportJobRecord record, CancellationToken cancellationToken)
        {
            long initialStartId = long.Parse(record.StartSurrogateId);
            long initialEndId = long.Parse(record.EndSurrogateId);

            var rangeQueue = new Queue<(long StartId, long EndId, int OomReductionCount)>();
            rangeQueue.Enqueue((initialStartId, initialEndId, 0));

            _logger.LogJobInformation(
                jobInfo,
                "Starting export with surrogate ID range. StartId={StartId}, EndId={EndId}, BatchSize={BatchSize}.",
                initialStartId,
                initialEndId,
                _effectiveBatchSize);

            while (rangeQueue.Count > 0 && !cancellationToken.IsCancellationRequested)
            {
                var workItem = rangeQueue.Dequeue();

                // Update record with the current sub-range so ExportJobTask queries the right window
                record.StartSurrogateId = workItem.StartId.ToString();
                record.EndSurrogateId = workItem.EndId.ToString();
                record.MaximumNumberOfResourcesPerQuery = (uint)_effectiveBatchSize;

                try
                {
                    IExportJobTask exportJobTask = _exportJobTaskFactory();
                    exportJobTask.UpdateExportJob = CreateUpdateExportJobCallback(jobInfo, ref record);
                    await exportJobTask.ExecuteAsync(record, WeakETag.FromVersionId("0"), cancellationToken);
                }
                catch (OutOfMemoryException oomEx)
                {
                    await SplitAndQueueSubRangesAsync(workItem, rangeQueue, jobInfo, record, oomEx, cancellationToken);
                    continue;
                }

                _logger.LogJobInformation(
                    jobInfo,
                    "Export range complete. RangeStart={RangeStart}, RangeEnd={RangeEnd}.",
                    workItem.StartId,
                    workItem.EndId);

                if (record.Output != null)
                {
                    jobInfo.Data = record.Output.Values.Sum(infos => infos.Sum(info => info.Count));
                }
            }
        }

        /// <summary>
        /// Cosmos/non-SQL path: On OOM, reduce batch size and retry the entire job.
        /// </summary>
        private async Task ExecuteWithBatchReductionAsync(JobInfo jobInfo, ExportJobRecord record, CancellationToken cancellationToken)
        {
            int oomReductionCount = 0;

            while (true)
            {
                IExportJobTask exportJobTask = _exportJobTaskFactory();
                exportJobTask.UpdateExportJob = CreateUpdateExportJobCallback(jobInfo, ref record);

                try
                {
                    await exportJobTask.ExecuteAsync(record, WeakETag.FromVersionId("0"), cancellationToken);
                    break;
                }
                catch (OutOfMemoryException oomEx)
                {
                    oomReductionCount++;

                    if (oomReductionCount > MaxOomReductionsBeforeSoftFail || !TryReduceEffectiveBatchSize())
                    {
                        _logger.LogJobError(
                            oomEx,
                            jobInfo,
                            "OutOfMemoryException during export could not be recovered after {AttemptCount} reductions. Final batch size was {BatchSize}.",
                            oomReductionCount,
                            _effectiveBatchSize);

                        ThrowOomJobFailure(record, oomEx);
                    }

                    record.MaximumNumberOfResourcesPerQuery = (uint)_effectiveBatchSize;

                    _logger.LogJobWarning(
                        oomEx,
                        jobInfo,
                        "OutOfMemoryException during export. Reducing batch size to {BatchSize} and retrying. ReductionAttempt={AttemptCount}/{MaxAttempts}.",
                        _effectiveBatchSize,
                        oomReductionCount,
                        MaxOomReductionsBeforeSoftFail);
                }
            }
        }

        private async Task SplitAndQueueSubRangesAsync(
            (long StartId, long EndId, int OomReductionCount) failedRange,
            Queue<(long StartId, long EndId, int OomReductionCount)> rangeQueue,
            JobInfo jobInfo,
            ExportJobRecord record,
            OutOfMemoryException oomEx,
            CancellationToken cancellationToken)
        {
            int previousBatchSize = _effectiveBatchSize;
            bool wasReduced = TryReduceEffectiveBatchSize();

            if (!wasReduced)
            {
                _logger.LogJobError(
                    oomEx,
                    jobInfo,
                    "OutOfMemoryException persisted during export. Batch size already at minimum {MinBatchSize}. RangeStart={StartId}, RangeEnd={EndId}.",
                    MinEffectiveBatchSize,
                    failedRange.StartId,
                    failedRange.EndId);

                ThrowOomJobFailure(record, oomEx);
            }

            int reductionCount = failedRange.OomReductionCount + 1;
            if (reductionCount > MaxOomReductionsBeforeSoftFail)
            {
                _logger.LogJobError(
                    oomEx,
                    jobInfo,
                    "OutOfMemoryException persisted after {MaxAttempts} reductions during export. CurrentBatchSize={CurrentBatchSize}, RangeStart={StartId}, RangeEnd={EndId}.",
                    MaxOomReductionsBeforeSoftFail,
                    _effectiveBatchSize,
                    failedRange.StartId,
                    failedRange.EndId);

                ThrowOomJobFailure(record, oomEx);
            }

            int rangeSize = _effectiveBatchSize;
            int numberOfRanges = Math.Max(1, (int)Math.Ceiling((double)previousBatchSize / _effectiveBatchSize));

            _logger.LogJobWarning(
                oomEx,
                jobInfo,
                "OutOfMemoryException during export. Splitting range StartId={StartId}, EndId={EndId}. ReductionAttempt={ReductionAttempt}/{MaxAttempts}, PreviousBatchSize={PreviousBatchSize}, NextBatchSize={NextBatchSize}.",
                failedRange.StartId,
                failedRange.EndId,
                reductionCount,
                MaxOomReductionsBeforeSoftFail,
                previousBatchSize,
                _effectiveBatchSize);

            // Determine the resource type for the range split query, matching the orchestrator's behavior.
            // For ExportType.All, the orchestrator assigns a single ResourceType per processing job (e.g. "Observation").
            // For ExportType.Patient, the orchestrator splits on Patient surrogate IDs regardless of _type filter.
            // Group export never has surrogate IDs, so it won't reach this code path.
            string resourceType = record.ExportType == ExportJobType.Patient
                ? KnownResourceTypes.Patient
                : record.ResourceType?.Split(',')[0];

            if (string.IsNullOrEmpty(resourceType))
            {
                _logger.LogJobError(
                    jobInfo,
                    "Cannot split surrogate range: resource type is not set on the export job record. ExportType={ExportType}, RangeStart={StartId}, RangeEnd={EndId}.",
                    record.ExportType,
                    failedRange.StartId,
                    failedRange.EndId);
                ThrowOomJobFailure(record, oomEx);
            }

            IReadOnlyList<(long StartId, long EndId, int Count)> subRanges;
            using (IScoped<ISearchService> searchService = _searchServiceFactory())
            {
                subRanges = await searchService.Value.GetSurrogateIdRanges(
                    resourceType,
                    failedRange.StartId,
                    failedRange.EndId,
                    rangeSize,
                    numberOfRanges,
                    true,
                    cancellationToken,
                    true);
            }

            if (subRanges == null || subRanges.Count == 0)
            {
                _logger.LogJobError(
                    jobInfo,
                    "Failed to split surrogate range after OOM. No sub-ranges returned for StartId={StartId}, EndId={EndId}.",
                    failedRange.StartId,
                    failedRange.EndId);
                ThrowOomJobFailure(record, oomEx);
            }

            foreach (var range in subRanges)
            {
                rangeQueue.Enqueue((range.StartId, range.EndId, reductionCount));
            }
        }

        private static void ThrowOomJobFailure(ExportJobRecord record, OutOfMemoryException oomEx)
        {
            record.FailureDetails = new JobFailureDetails(
                string.Format(Core.Resources.ExportOutOfMemoryException, record.MaximumNumberOfResourcesPerQuery),
                HttpStatusCode.RequestEntityTooLarge,
                string.Concat(oomEx.Message + "\n\r" + oomEx.StackTrace));
            record.Status = OperationStatus.Failed;
            throw new JobExecutionException(record.FailureDetails.FailureReason, record, false);
        }

        private bool TryReduceEffectiveBatchSize()
        {
            if (_effectiveBatchSize <= MinEffectiveBatchSize)
            {
                return false;
            }

            int reducedBatchSize = Math.Max(MinEffectiveBatchSize, _effectiveBatchSize / OomReductionFactor);
            reducedBatchSize = Math.Min(reducedBatchSize, _effectiveBatchSize);

            if (reducedBatchSize == _effectiveBatchSize)
            {
                return false;
            }

            _effectiveBatchSize = reducedBatchSize;
            return true;
        }

        private static string ProcessResult(JobInfo jobInfo, ExportJobRecord record)
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
                    throw new JobExecutionException(record.FailureDetails.FailureReason, record, false);
                case OperationStatus.Canceled:
                    throw new OperationCanceledException($"[GroupId:{jobInfo.GroupId}/JobId:{jobInfo.Id}] Export job cancelled.");
                case OperationStatus.Queued:
                case OperationStatus.Running:
                    // If code works as designed, this exception shouldn't be reached
                    throw new JobExecutionException($"[GroupId:{jobInfo.GroupId}/JobId:{jobInfo.Id}] Export job finished in non-terminal state. See logs from ExportJobTask.", record, false);
                default:
                    // If code works as designed, this exception shouldn't be reached
                    throw new JobExecutionException($"[GroupId:{jobInfo.GroupId}/JobId:{jobInfo.Id}] Job status not set.", false);
            }
        }

        private Func<ExportJobRecord, WeakETag, CancellationToken, Task<ExportJobOutcome>> CreateUpdateExportJobCallback(JobInfo jobInfo, ref ExportJobRecord record)
        {
            // Capture record by reference via a local so the callback can update it for the caller.
            var capturedRecord = record;

            return async (ExportJobRecord exportJobRecord, WeakETag weakETag, CancellationToken innerCancellationToken) =>
            {
                capturedRecord = exportJobRecord;

                if (capturedRecord.Status == OperationStatus.Running)
                {
                    // Update Export Job is called in three scenarios:
                    // 1. A page of search results is processed and there is a continuation token.
                    // 2. A filter has finished being used by a search
                    // 3. The export job has reached a terminal state
                    // This section is only reached in cases 1 & 2.
                    // For jobs running in parallel such that each job only processes one page of results this section will never be reached as there are no filters and no continuation tokens.
                    // For single threaded jobs this section will complete a job after every page of results is processed and make a new job for the next page.
                    // This will allow for saving intermediate states of the job.

                    const int maxRetries = 3;
                    const int initialRetryDelaySeconds = 2;

                    // Create a retry policy with exponential backoff for handling conflict errors when adding next job.
                    AsyncRetryPolicy retryPolicy = Policy
                        .Handle<Exception>(ex => ex.Message.Contains("Resource with specified id or name already exists", StringComparison.InvariantCultureIgnoreCase))
                        .WaitAndRetryAsync(
                            retryCount: maxRetries,
                            sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt - 1) * initialRetryDelaySeconds),
                            onRetry: (exception, timeSpan, retryCount, context) =>
                            {
                                _logger.LogWarning($"Attempt {retryCount} failed with 409 Conflict. Retrying in {timeSpan.TotalSeconds} seconds. Exception: {exception.Message}");
                            });

                    try
                    {
                        await retryPolicy.ExecuteAsync(async () =>
                        {
                            var existingJobs = await _queueClient.GetJobByGroupIdAsync(QueueType.Export, jobInfo.GroupId, false, innerCancellationToken);

                            // Only queue new jobs if group has no canceled jobs. This ensures canceled jobs won't continue to queue new jobs.
                            if (!existingJobs.Any(job => job.Status == JobStatus.Cancelled || job.CancelRequested))
                            {
                                var definition = jobInfo.DeserializeDefinition<ExportJobRecord>();

                                // Checks that a follow-up job has not already been made.
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
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Unhandled exception during UpdateExportJob: {ex.Message}");
                        throw;
                    }

                    capturedRecord.Status = OperationStatus.Completed;

                    // TODO: Ideally we would process predefined pages of data like SQL vs pagination through continuation tokens/this exception.
                    throw new JobSegmentCompletedException();
                }

                return new ExportJobOutcome(exportJobRecord, weakETag);
            };
        }
    }
}
