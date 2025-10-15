// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance.Serialization;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations;

public abstract class FhirOperationDataStoreBase : IFhirOperationDataStore
{
    private readonly IQueueClient _queueClient;
    private readonly JsonSerializerSettings _jsonSerializerSettings;
    private readonly ILogger<FhirOperationDataStoreBase> _logger;

    protected FhirOperationDataStoreBase(IQueueClient queueClient, ILoggerFactory loggerFactory)
    {
        _queueClient = EnsureArg.IsNotNull(queueClient, nameof(queueClient));
        _logger = EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory)).CreateLogger<FhirOperationDataStoreBase>();

        _jsonSerializerSettings = new JsonSerializerSettings
        {
            Converters = new List<JsonConverter>
            {
                new EnumLiteralJsonConverter(),
            },
        };
    }

    public virtual async Task<ExportJobOutcome> CreateExportJobAsync(ExportJobRecord jobRecord, CancellationToken cancellationToken)
    {
        var clone = jobRecord.Clone();
        clone.QueuedTime = DateTime.Parse("1900-01-01");
        var results = await _queueClient.EnqueueAsync(QueueType.Export, cancellationToken, definitions: clone);

        if (results.Count != 1)
        {
            throw new OperationFailedException(string.Format(Core.Resources.OperationFailed, OperationsConstants.Export, "Failed to create export job."), HttpStatusCode.InternalServerError);
        }

        var jobInfo = results[0];
        return CreateExportJobOutcome(jobInfo, clone);
    }

    public virtual async Task<ExportJobOutcome> GetExportJobByIdAsync(string id, CancellationToken cancellationToken)
    {
        EnsureArg.IsNotNullOrWhiteSpace(id, nameof(id));

        if (!long.TryParse(id, out var jobId))
        {
            throw new JobNotFoundException(string.Format(Core.Resources.JobNotFound, id));
        }

        var jobInfo = await _queueClient.GetJobByIdAsync(QueueType.Export, jobId, true, cancellationToken);

        if (jobInfo == null)
        {
            throw new JobNotFoundException(string.Format(Core.Resources.JobNotFound, id));
        }

        var def = jobInfo.Definition;
        var status = jobInfo.Status;
        var result = jobInfo.Result;
        var record = jobInfo.DeserializeDefinition<ExportJobRecord>();

        if (status == JobStatus.Completed)
        {
            var groupJobs = (await _queueClient.GetJobByGroupIdAsync(QueueType.Export, jobInfo.GroupId, false, cancellationToken)).ToList();
            var inFlightJobsExist = groupJobs.Where(x => x.Id != jobInfo.Id).Any(x => x.Status == JobStatus.Running || x.Status == JobStatus.Created || (x.EndDate != null && x.EndDate > DateTime.UtcNow.AddSeconds(-15)));
            var cancelledJobsExist = groupJobs.Where(x => x.Id != jobInfo.Id).Any(x => x.Status == JobStatus.Cancelled || (x.Status == JobStatus.Running && x.CancelRequested));
            var failedJobsExist = groupJobs.Where(x => x.Id != jobInfo.Id).Any(x => x.Status == JobStatus.Failed);

            if (cancelledJobsExist && !failedJobsExist)
            {
                status = JobStatus.Cancelled;
            }
            else if (failedJobsExist)
            {
                foreach (var job in groupJobs.Where(x => x.Id != jobInfo.Id && x.Status == JobStatus.Failed))
                {
                    if (!string.IsNullOrEmpty(job.Result) && !job.Result.Equals("null", StringComparison.OrdinalIgnoreCase))
                    {
                        var processResult = JsonConvert.DeserializeObject<ExportJobRecord>(job.Result);
                        if (processResult.FailureDetails != null)
                        {
                            if (record.FailureDetails == null)
                            {
                                record.FailureDetails = processResult.FailureDetails;
                            }
                            else if (!processResult.FailureDetails.FailureReason.Equals(record.FailureDetails.FailureReason, StringComparison.OrdinalIgnoreCase))
                            {
                                record.FailureDetails = new JobFailureDetails(record.FailureDetails.FailureReason + "\r\n" + processResult.FailureDetails.FailureReason, record.FailureDetails.FailureStatusCode);
                            }
                        }
                    }
                    else
                    {
                        if (record.FailureDetails == null)
                        {
                            record.FailureDetails = new JobFailureDetails("Processing job had no results", HttpStatusCode.InternalServerError);
                        }
                        else
                        {
                            record.FailureDetails = new JobFailureDetails(record.FailureDetails.FailureReason + "\r\nProcessing job had no results", HttpStatusCode.InternalServerError);
                        }
                    }
                }

                record.Status = OperationStatus.Failed;
                status = JobStatus.Failed;
                result = JsonConvert.SerializeObject(record);
            }
            else if (!inFlightJobsExist) // no failures here
            {
                foreach (var job in groupJobs.Where(_ => _.Id != jobInfo.Id))
                {
                    if (!string.IsNullOrEmpty(job.Result) && !job.Result.Equals("null", StringComparison.OrdinalIgnoreCase))
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
                    }
                }

                record.Status = OperationStatus.Completed;
                status = JobStatus.Completed;
                result = JsonConvert.SerializeObject(record);
            }
            else
            {
                status = JobStatus.Running;
            }
        }

        return CreateExportJobOutcome(jobId, result ?? def, jobInfo.Version, (byte)status, jobInfo.CreateDate);
    }

    public virtual async Task<ExportJobOutcome> UpdateExportJobAsync(ExportJobRecord jobRecord, WeakETag eTag, CancellationToken cancellationToken)
    {
        EnsureArg.IsNotNull(jobRecord, nameof(jobRecord));

        if (jobRecord.Status != OperationStatus.Canceled)
        {
            throw new NotSupportedException($"Calls to this method with status={jobRecord.Status} are deprecated.");
        }

        eTag ??= WeakETag.FromVersionId("0");

        try
        {
            var jobWithGroupId = await _queueClient.GetJobByIdAsync(QueueType.Export, long.Parse(jobRecord.Id), false, cancellationToken);
            await _queueClient.CancelJobByGroupIdAsync(QueueType.Export, jobWithGroupId.GroupId, cancellationToken);
        }
        catch (JobNotExistException ex)
        {
            throw new JobNotFoundException(ex.Message);
        }

        return new ExportJobOutcome(jobRecord, eTag);
    }

    public virtual async Task<IReadOnlyCollection<ExportJobOutcome>> AcquireExportJobsAsync(ushort numberOfJobsToAcquire, TimeSpan jobHeartbeatTimeoutThreshold, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<JobInfo> jobInfos = await _queueClient.DequeueJobsAsync(QueueType.Export, numberOfJobsToAcquire, Environment.MachineName, (int)jobHeartbeatTimeoutThreshold.TotalSeconds, cancellationToken);

        var acquiredJobs = new List<ExportJobOutcome>();

        foreach (var job in jobInfos)
        {
            var id = job.Id;
            var def = job.Definition;
            var version = job.Version;
            var status = job.Status;
            var result = job.Result;
            var createDate = job.CreateDate;
            var rawJobRecord = result ?? def;
            acquiredJobs.Add(CreateExportJobOutcome(id, rawJobRecord, version, (byte)status, createDate));
        }

        return acquiredJobs;
    }

    public virtual async Task<ReindexJobWrapper> CreateReindexJobAsync(ReindexJobRecord jobRecord, CancellationToken cancellationToken)
    {
        var def = new ReindexOrchestratorJobDefinition()
        {
            TypeId = (int)JobType.ReindexOrchestrator,
            MaximumNumberOfResourcesPerQuery = jobRecord.MaximumNumberOfResourcesPerQuery,
            MaximumNumberOfResourcesPerWrite = jobRecord.MaximumNumberOfResourcesPerWrite,
            ResourceTypeSearchParameterHashMap = jobRecord.ResourceTypeSearchParameterHashMap,
            Id = jobRecord.Id,
        };

        // If no Active jobs, we are safe to queue
        _logger.LogInformation($"Queueing reindex job with definition: {def}");
        var results = await _queueClient.EnqueueAsync(QueueType.Reindex, cancellationToken, definitions: def);

        var jobInfo = results[0];
        jobRecord.Id = jobInfo.Id.ToString();
        jobRecord.GroupId = jobInfo.GroupId;
        jobRecord.Status = OperationStatus.Queued;
        return new ReindexJobWrapper(jobRecord, WeakETag.FromVersionId(jobInfo.Version.ToString()));
    }

    public virtual async Task<ReindexJobWrapper> UpdateReindexJobAsync(ReindexJobRecord jobRecord, WeakETag eTag, CancellationToken cancellationToken)
    {
        EnsureArg.IsNotNull(jobRecord, nameof(jobRecord));

        if (jobRecord.Status != OperationStatus.Canceled)
        {
            throw new NotSupportedException($"Calls to this method with status={jobRecord.Status} are deprecated.");
        }

        eTag ??= WeakETag.FromVersionId("0");

        jobRecord.LastModified = Clock.UtcNow;

        try
        {
            var jobWithGroupId = await _queueClient.GetJobByIdAsync((byte)QueueType.Reindex, long.Parse(jobRecord.Id), false, cancellationToken);
            await _queueClient.CancelJobByGroupIdAsync((byte)QueueType.Reindex, jobWithGroupId.GroupId, cancellationToken);
        }
        catch (JobNotExistException ex)
        {
            throw new JobNotFoundException(ex.Message);
        }

        return new ReindexJobWrapper(jobRecord, eTag);
    }

    public virtual async Task<IReadOnlyCollection<ReindexJobWrapper>> AcquireReindexJobsAsync(ushort maximumNumberOfConcurrentJobsAllowed, TimeSpan jobHeartbeatTimeoutThreshold, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<JobInfo> jobInfos = await _queueClient.DequeueJobsAsync((byte)QueueType.Reindex, maximumNumberOfConcurrentJobsAllowed, Environment.MachineName, (int)jobHeartbeatTimeoutThreshold.TotalSeconds, cancellationToken);

        var acquiredJobs = new List<ReindexJobWrapper>();

        foreach (var job in jobInfos)
        {
            var jobRecord = await GetReindexJobByIdAsync(job.Id.ToString(), cancellationToken);
            acquiredJobs.Add(jobRecord);
        }

        return acquiredJobs;
    }

    /// <summary>
    /// Returns a reindex orchestrator job by id.
    /// </summary>
    /// <param name="jobId">Assumed to be a ReindexOrchestratorJob.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>ReindexJobRecord in a ReindexJobWrapper.</returns>
    /// <exception cref="JobNotFoundException">Throws when job is not found or if job is not a ReindexOrchestratorJob.</exception>
    public virtual async Task<ReindexJobWrapper> GetReindexJobByIdAsync(string jobId, CancellationToken cancellationToken)
    {
        EnsureArg.IsNotNullOrWhiteSpace(jobId, nameof(jobId));

        if (!long.TryParse(jobId, out var id))
        {
            throw new JobNotFoundException(string.Format(Core.Resources.JobNotFound, jobId));
        }

        var jobInfo = await _queueClient.GetJobByIdAsync((byte)QueueType.Reindex, id, true, cancellationToken);

        if (jobInfo == null)
        {
            throw new JobNotFoundException(string.Format(Core.Resources.JobNotFound, id));
        }

        var status = jobInfo.Status;
        var result = jobInfo.Result;
        var serializedJobResult = !string.IsNullOrEmpty(result) && !result.Equals("null", StringComparison.OrdinalIgnoreCase)
            ? JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(result)
            : null;
        ReindexJobRecord record;
        try
        {
            record = JsonConvert.DeserializeObject<ReindexJobRecord>(jobInfo.Definition);
        }
        catch
        {
            throw new JobNotFoundException(string.Format(Core.Resources.JobNotReindexOrchestratorJob, id));
        }

        record.Id = jobInfo.Id.ToString();
        record.GroupId = jobInfo.GroupId;

        var groupJobs = (await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Reindex, jobInfo.GroupId, true, cancellationToken)).ToList();
        var inFlightJobsExist = groupJobs.Where(x => x.Id != jobInfo.Id).Any(x => x.Status == JobStatus.Running || x.Status == JobStatus.Created);
        var cancelledJobsExist = groupJobs.Where(x => x.Id != jobInfo.Id).Any(x => x.Status == JobStatus.Cancelled || (x.Status == JobStatus.Running && x.CancelRequested));
        var failedJobsExist = groupJobs.Where(x => x.Id != jobInfo.Id).Any(x => x.Status == JobStatus.Failed);

        if (cancelledJobsExist && !failedJobsExist && !inFlightJobsExist)
        {
            status = JobStatus.Cancelled;
        }

        if (failedJobsExist)
        {
            foreach (var job in groupJobs.Where(x => x.Id != jobInfo.Id && x.Status == JobStatus.Failed))
            {
                if (!string.IsNullOrEmpty(job.Result) && !job.Result.Equals("null", StringComparison.OrdinalIgnoreCase))
                {
                    var processResult = JsonConvert.DeserializeObject<ReindexProcessingJobErrorResult>(job.Result);
                    if (!string.IsNullOrEmpty(processResult.Message))
                    {
                        if (record.FailureDetails == null)
                        {
                            record.FailureDetails = new JobFailureDetails(processResult.Message, HttpStatusCode.InternalServerError);
                        }
                        else if (!processResult.Message.Contains(record.FailureDetails.FailureReason, StringComparison.OrdinalIgnoreCase))
                        {
                            record.FailureDetails = new JobFailureDetails(record.FailureDetails.FailureReason + "\r\n" + processResult.Message, record.FailureDetails.FailureStatusCode);
                        }
                    }
                }
                else
                {
                    if (record.FailureDetails == null)
                    {
                        record.FailureDetails = new JobFailureDetails("Processing job had no results", HttpStatusCode.InternalServerError);
                    }
                    else
                    {
                        record.FailureDetails = new JobFailureDetails(record.FailureDetails.FailureReason + "\r\nProcessing job had no results", HttpStatusCode.InternalServerError);
                    }
                }
            }

            // Only mark as failed if no in-flight jobs exist
            if (!inFlightJobsExist)
            {
                record.Status = OperationStatus.Failed;
                status = JobStatus.Failed;
            }
        }

        PopulateReindexJobRecordDataFromJobs(jobInfo, groupJobs, ref record);

        record.LastModified = jobInfo.HeartbeatDateTime;
        record.QueuedTime = jobInfo.CreateDate;

        if (serializedJobResult?.Error != null)
        {
            var errorMessage = string.Empty;

            foreach (var error in serializedJobResult.Error)
            {
                if (error.Diagnostics != null)
                {
                    errorMessage += error.Diagnostics + " ";
                }

                if (error.Severity == OperationOutcomeConstants.IssueSeverity.Error && !inFlightJobsExist)
                {
                    status = JobStatus.Failed;
                }
            }

            if (!string.IsNullOrEmpty(errorMessage))
            {
                record.FailureDetails = new JobFailureDetails(errorMessage, HttpStatusCode.InternalServerError);
            }
        }

        if (status == JobStatus.Failed && record.FailureDetails == null)
        {
            record.FailureDetails = new JobFailureDetails("Reindex failed with unknown error. Please resubmit to try again.", HttpStatusCode.InternalServerError);
        }

        switch (status)
        {
            case JobStatus.Created:
                record.Status = OperationStatus.Queued;
                break;
            case JobStatus.Running:
                record.Status = OperationStatus.Running;
                break;
            case JobStatus.Cancelled:
                record.Status = OperationStatus.Canceled;
                break;
            case JobStatus.Failed:
                record.Status = OperationStatus.Failed;
                break;
            case JobStatus.Completed:
                record.Status = OperationStatus.Completed;
                break;
            default:
                record.Status = OperationStatus.Unknown;
                break;
        }

        return new ReindexJobWrapper(record, WeakETag.FromVersionId(jobInfo.Version.ToString()));
    }

    private void PopulateReindexJobRecordDataFromJobs(JobInfo jobInfo, List<JobInfo> groupJobs, ref ReindexJobRecord record)
    {
        // Check the first child job's result
        var subJob = groupJobs.Where(x => x.Id != jobInfo.Id).FirstOrDefault();
        IReadOnlyCollection<string> processingJob = null;
        if (subJob?.Result != null)
        {
            try
            {
                processingJob = JsonConvert.DeserializeObject<ReindexProcessingJobResult>(subJob.Result)?.SearchParameterUrls;
            }
            catch (JsonException ex)
            {
                // Log but continue since this is optional data
                _logger.LogWarning(ex, "Failed to deserialize processing job result for job {JobId}", subJob.Id);
            }
        }

        if (processingJob != null)
        {
            foreach (var sp in processingJob)
            {
                record.SearchParams.Add(sp);
            }
        }

        foreach (var job in groupJobs.Where(x => x.Id != jobInfo.GroupId))
        {
            ReindexProcessingJobResult jobResult = null;
            ReindexProcessingJobDefinition jobDefinition = null;

            // Safely deserialize Result
            if (!string.IsNullOrEmpty(job.Result))
            {
                try
                {
                    jobResult = JsonConvert.DeserializeObject<ReindexProcessingJobResult>(job.Result);
                }
                catch (JsonException ex)
                {
                    // Log the error but continue processing
                    _logger.LogError(ex, "Failed to deserialize job result for job {JobId}", job.Id);
                    continue;
                }
            }

            // Safely deserialize Definition
            if (!string.IsNullOrEmpty(job.Definition))
            {
                try
                {
                    jobDefinition = JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(job.Definition);
                }
                catch (JsonException ex)
                {
                    // Log the error but continue processing
                    _logger.LogError(ex, "Failed to deserialize job definition for job {JobId}", job.Id);
                    continue;
                }
            }
            else
            {
                _logger.LogError("Job definition is null for job {JobId}", job.Id);
            }

            if (jobDefinition?.ResourceType != null)
            {
                // Aggregate counts instead of ignoring duplicates
                if (record.ResourceCounts.TryGetValue(jobDefinition.ResourceType, out var existing))
                {
                    existing.Count += jobDefinition.ResourceCount.Count;
                }
                else
                {
                    record.ResourceCounts.TryAdd(jobDefinition.ResourceType, jobDefinition.ResourceCount);
                }

                // Add to resources list only once
                if (!record.Resources.Contains(jobDefinition.ResourceType))
                {
                    record.Resources.Add(jobDefinition.ResourceType);
                }
            }

            if (jobResult != null)
            {
                if (job.Status == JobStatus.Completed)
                {
                    record.Progress += jobResult.SucceededResourceCount;
                }

                record.Count += jobResult.SucceededResourceCount + jobResult.FailedResourceCount;
            }
        }
    }

    public virtual async Task<(bool found, string id)> CheckActiveReindexJobsAsync(CancellationToken cancellationToken)
    {
        var activeJobs = await _queueClient.GetActiveJobsByQueueTypeAsync((byte)QueueType.Reindex, true, cancellationToken);
        var firstActiveJob = activeJobs.Count > 0 ? activeJobs[0] : null;
        return (firstActiveJob != null, firstActiveJob?.Id.ToString() ?? null);
    }

    private ExportJobOutcome CreateExportJobOutcome(long jobId, string rawJobRecord, long version, byte status, DateTime createDate)
    {
        try
        {
            var exportJobRecord = JsonConvert.DeserializeObject<ExportJobRecord>(rawJobRecord, _jsonSerializerSettings);
            return CreateExportJobOutcome(jobId, exportJobRecord, version, status, createDate);
        }
        catch (Exception ex)
        {
            throw new JsonSerializationException($"Error deserializing {rawJobRecord}", ex);
        }
    }

    private static ExportJobOutcome CreateExportJobOutcome(JobInfo jobInfo, ExportJobRecord jobRecord)
    {
        return CreateExportJobOutcome(jobInfo.Id, jobRecord, jobInfo.Version, (byte)jobInfo.Status, jobInfo.CreateDate);
    }

    private static ExportJobOutcome CreateExportJobOutcome(long jobId, ExportJobRecord jobRecord, long version, byte status, DateTime createDate)
    {
        jobRecord.Id = jobId.ToString();
        jobRecord.QueuedTime = createDate;
        jobRecord.Status = (OperationStatus)status;
        var etag = GetRowVersionAsEtag(BitConverter.GetBytes(version));
        return new ExportJobOutcome(jobRecord, etag);
    }

    private static WeakETag GetRowVersionAsEtag(byte[] rowVersionAsBytes)
    {
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(rowVersionAsBytes);
        }

        var rowVersionAsDecimalString = BitConverter.ToInt64(rowVersionAsBytes, startIndex: 0).ToString();

        return WeakETag.FromVersionId(rowVersionAsDecimalString);
    }
}
