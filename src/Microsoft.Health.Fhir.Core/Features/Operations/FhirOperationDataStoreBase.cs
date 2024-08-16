// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Conformance.Serialization;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;
using Polly;

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
            Converters = [new EnumLiteralJsonConverter()],
        };
    }

    public virtual async Task<ExportJobOutcome> CreateExportJobAsync(ExportJobRecord jobRecord, CancellationToken cancellationToken)
    {
        var clone = jobRecord.Clone();
        clone.QueuedTime = DateTime.Parse("1900-01-01");
        var results = await _queueClient.EnqueueAsync(QueueType.Export, cancellationToken, isCompleted: clone.Status == OperationStatus.Completed, definitions: clone);

        if (results.Count != 1)
        {
            throw new OperationFailedException(string.Format(Core.Resources.OperationFailed, OperationsConstants.Export, "Failed to create export job."), HttpStatusCode.InternalServerError);
        }

        var jobInfo = results[0];
        return CreateExportJobOutcome(jobInfo, clone);
    }

    /// <summary>
    /// Retrieves the results of an $import or $export "orchestrated job" by the orchestrator id. This idis what is returned to the API caller.
    /// </summary>
    /// <param name="queueType">The type of the queue where the job is located.</param>
    /// <param name="orchestratorJobId">The ID of the orchestrator job.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A tuple containing the status of the job, the orchestrator job information, and a list of group jobs.</returns>
    /// <exception cref="JobNotFoundException">Thrown when the job is not found or is archived.</exception>
    internal async Task<(JobStatus? Status, JobInfo OrchetratorJob, List<JobInfo> ProcessingJobs)> GetOrchestratedJobResultsByIdAsync(QueueType queueType, string orchestratorJobId, CancellationToken cancellationToken)
    {
        EnsureArg.IsNotNullOrWhiteSpace(orchestratorJobId, nameof(orchestratorJobId));

        if (!long.TryParse(orchestratorJobId, out var orchestratorJobIdParsed))
        {
            throw new JobNotFoundException(string.Format(Core.Resources.JobNotFound, orchestratorJobId));
        }

        JobInfo orchestratorJob = await _queueClient.GetJobByIdAsync(queueType, orchestratorJobIdParsed, true, cancellationToken);

        if (orchestratorJob == null || orchestratorJob.Status == JobStatus.Archived || orchestratorJob.GroupId != orchestratorJobIdParsed)
        {
            throw new JobNotFoundException(string.Format(Core.Resources.JobNotFound, orchestratorJobId));
        }
        else if (orchestratorJob.Status != JobStatus.Completed)
        {
            return (orchestratorJob.Status, orchestratorJob, []);
        }

        // Process orchestrator completed (child processing jobs may still be running).
        var start = Stopwatch.StartNew();
        var jobs = (await _queueClient.GetJobByGroupIdAsync(queueType, orchestratorJob.GroupId, true, cancellationToken)).Where(x => x.Id != orchestratorJob.Id).ToList();
        await Task.Delay(TimeSpan.FromSeconds(start.Elapsed.TotalSeconds > 6 ? 60 : start.Elapsed.TotalSeconds * 10), cancellationToken); // throttle to avoid misuse.
        var inFlightJobsExist = jobs.Any(x => x.Status == JobStatus.Running || x.Status == JobStatus.Created);
        var cancelledJobsExist = jobs.Any(x => x.Status == JobStatus.Cancelled || x.CancelRequested);
        var failedJobsExist = jobs.Any(x => x.Status == JobStatus.Failed && !x.CancelRequested);

        if (cancelledJobsExist && !failedJobsExist)
        {
            return (JobStatus.Cancelled, orchestratorJob, jobs);
        }
        else if (failedJobsExist)
        {
            return (JobStatus.Failed, orchestratorJob, jobs);
        }
        else // no failures here
        {
            JobStatus jobStatus = inFlightJobsExist ? JobStatus.Running : JobStatus.Completed;
            return (jobStatus, orchestratorJob, jobs);
        }
    }

    public virtual async Task<ExportJobOutcome> GetExportJobByIdAsync(string id, CancellationToken cancellationToken)
    {
        var jobs = await GetOrchestratedJobResultsByIdAsync(QueueType.Export, id, cancellationToken);
        var record = jobs.OrchetratorJob.DeserializeDefinition<ExportJobRecord>();
        var result = jobs.OrchetratorJob.Result;

        if (jobs.Status == JobStatus.Failed)
        {
            foreach (var job in jobs.ProcessingJobs.Where(x => x.Status == JobStatus.Failed))
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
            result = JsonConvert.SerializeObject(record);
        }
        else if (jobs.Status == JobStatus.Cancelled)
        {
            record.Status = OperationStatus.Canceled;
            result = JsonConvert.SerializeObject(record);
        }
        else if (jobs.Status == JobStatus.Completed)
        {
            foreach (var job in jobs.ProcessingJobs)
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
            result = JsonConvert.SerializeObject(record);
        }

        return CreateExportJobOutcome(jobs.OrchetratorJob.Id, result ?? jobs.OrchetratorJob.Definition, jobs.OrchetratorJob.Version, (byte)jobs.Status, jobs.OrchetratorJob.CreateDate);
    }

    public virtual Task<(JobStatus? Status, JobInfo OrchetratorJob, List<JobInfo> ProcessingJobs)> GetImportJobByIdAsync(string id, CancellationToken cancellationToken)
    {
        // Import is only enabled on SQL datastore.
        throw new NotImplementedException();
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

    public async Task CancelOrchestratedJob(QueueType queueType, string jobId, CancellationToken cancellationToken)
    {
        var jobsInfo = await GetOrchestratedJobResultsByIdAsync(queueType, jobId, cancellationToken);

        // If the job is already completed for any reason, return conflict status.
        if (jobsInfo.Status == JobStatus.Completed || jobsInfo.Status == JobStatus.Failed)
        {
            throw new OperationFailedException(Core.Resources.ImportOperationCompleted, HttpStatusCode.Conflict);
        }

        // If the job is already cancelled, return bad request status.
        // #TODO - make sure no jobs are in created or running. If so try to cancel.
        if (jobsInfo.Status == JobStatus.Cancelled)
        {
            throw new OperationFailedException(Core.Resources.ImportOperationCompleted, HttpStatusCode.BadRequest);
        }

        // #TODO - ensure group id matches jobs ID.
        await _queueClient.CancelJobByGroupIdAsync(queueType, jobsInfo.OrchetratorJob.Id, cancellationToken);
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

    public abstract Task<ReindexJobWrapper> CreateReindexJobAsync(ReindexJobRecord jobRecord, CancellationToken cancellationToken);

    public abstract Task<ReindexJobWrapper> UpdateReindexJobAsync(ReindexJobRecord jobRecord, WeakETag eTag, CancellationToken cancellationToken);

    public abstract Task<IReadOnlyCollection<ReindexJobWrapper>> AcquireReindexJobsAsync(ushort maximumNumberOfConcurrentJobsAllowed, TimeSpan jobHeartbeatTimeoutThreshold, CancellationToken cancellationToken);

    public abstract Task<ReindexJobWrapper> GetReindexJobByIdAsync(string jobId, CancellationToken cancellationToken);

    public abstract Task<(bool found, string id)> CheckActiveReindexJobsAsync(CancellationToken cancellationToken);

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
