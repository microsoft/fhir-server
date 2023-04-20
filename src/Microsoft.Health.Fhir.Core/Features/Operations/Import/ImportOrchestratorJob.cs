// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;
using JobStatus = Microsoft.Health.JobManagement.JobStatus;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    [JobTypeId((int)JobType.ImportOrchestrator)]
    public class ImportOrchestratorJob : IJob
    {
        private const long DefaultResourceSizePerByte = 64;
        public const int BytesToRead = 10000 * 2000; // each job should handle about 10000 resources. with about 2000 bytes per resource

        private readonly IMediator _mediator;
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;
        private readonly IImportOrchestratorJobDataStoreOperation _importOrchestratorJobDataStoreOperation;
        private readonly IQueueClient _queueClient;
        private ImportTaskConfiguration _importConfiguration;
        private ILogger<ImportOrchestratorJob> _logger;
        private IIntegrationDataStoreClient _integrationDataStoreClient;

        public ImportOrchestratorJob(
            IMediator mediator,
            RequestContextAccessor<IFhirRequestContext> contextAccessor,
            IImportOrchestratorJobDataStoreOperation importOrchestratorJobDataStoreOperation,
            IIntegrationDataStoreClient integrationDataStoreClient,
            IQueueClient queueClient,
            IOptions<ImportTaskConfiguration> importConfiguration,
            ILoggerFactory loggerFactory)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));
            EnsureArg.IsNotNull(importOrchestratorJobDataStoreOperation, nameof(importOrchestratorJobDataStoreOperation));
            EnsureArg.IsNotNull(integrationDataStoreClient, nameof(integrationDataStoreClient));
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            EnsureArg.IsNotNull(importConfiguration?.Value, nameof(importConfiguration));
            EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory));

            _mediator = mediator;
            _contextAccessor = contextAccessor;
            _importOrchestratorJobDataStoreOperation = importOrchestratorJobDataStoreOperation;
            _integrationDataStoreClient = integrationDataStoreClient;
            _queueClient = queueClient;
            _importConfiguration = importConfiguration.Value;
            _logger = loggerFactory.CreateLogger<ImportOrchestratorJob>();

            PollingFrequencyInSeconds = _importConfiguration.PollingFrequencyInSeconds;
        }

        public int PollingFrequencyInSeconds { get; set; }

        public async Task<string> ExecuteAsync(JobInfo jobInfo, IProgress<string> progress, CancellationToken cancellationToken)
        {
            ImportOrchestratorJobInputData inputData = JsonConvert.DeserializeObject<ImportOrchestratorJobInputData>(jobInfo.Definition);
            ImportOrchestratorJobResult currentResult = string.IsNullOrEmpty(jobInfo.Result) ? new ImportOrchestratorJobResult() : JsonConvert.DeserializeObject<ImportOrchestratorJobResult>(jobInfo.Result);

            var fhirRequestContext = new FhirRequestContext(
                    method: "Import",
                    uriString: inputData.RequestUri.ToString(),
                    baseUriString: inputData.BaseUri.ToString(),
                    correlationId: jobInfo.Id.ToString(),
                    requestHeaders: new Dictionary<string, StringValues>(),
                    responseHeaders: new Dictionary<string, StringValues>())
            {
                IsBackgroundTask = true,
            };

            _contextAccessor.RequestContext = fhirRequestContext;

            currentResult.Request = inputData.RequestUri.ToString();
            currentResult.TransactionTime = inputData.CreateTime;

            ImportOrchestratorJobErrorResult errorResult = null;

            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException();
                }

                if (currentResult.Progress == ImportOrchestratorJobProgress.Initialized)
                {
                    await ValidateResourcesAsync(inputData, cancellationToken);

                    currentResult.Progress = ImportOrchestratorJobProgress.InputResourcesValidated;
                    progress.Report(JsonConvert.SerializeObject(currentResult));

                    _logger.LogInformation("Input Resources Validated");
                }

                if (currentResult.Progress == ImportOrchestratorJobProgress.InputResourcesValidated)
                {
                    await _importOrchestratorJobDataStoreOperation.PreprocessAsync(cancellationToken);

                    currentResult.Progress = ImportOrchestratorJobProgress.PreprocessCompleted;
                    currentResult.CurrentSequenceId = inputData.StartSequenceId;
                    progress.Report(JsonConvert.SerializeObject(currentResult));

                    _logger.LogInformation("Preprocess Completed");
                }

                if (currentResult.Progress == ImportOrchestratorJobProgress.PreprocessCompleted)
                {
                    await ExecuteImportProcessingJobAsync(progress, jobInfo, inputData, currentResult, inputData.StartSequenceId == 0, cancellationToken);
                    currentResult.Progress = ImportOrchestratorJobProgress.SubJobsCompleted;
                    progress.Report(JsonConvert.SerializeObject(currentResult));

                    _logger.LogInformation("SubJobs Completed");
                }
            }
            catch (TaskCanceledException taskCanceledEx)
            {
                _logger.LogInformation(taskCanceledEx, "Import job canceled. {Message}", taskCanceledEx.Message);

                errorResult = new ImportOrchestratorJobErrorResult()
                {
                    HttpStatusCode = HttpStatusCode.BadRequest,
                    ErrorMessage = taskCanceledEx.Message,
                };

                // Processing jobs has been cancelled by CancelImportRequestHandler
                await WaitCancelledJobCompletedAsync(jobInfo);
                await SendImportMetricsNotification(JobStatus.Cancelled, jobInfo, inputData, currentResult);
            }
            catch (OperationCanceledException canceledEx)
            {
                _logger.LogInformation(canceledEx, "Import job canceled. {Message}", canceledEx.Message);

                errorResult = new ImportOrchestratorJobErrorResult()
                {
                    HttpStatusCode = HttpStatusCode.BadRequest,
                    ErrorMessage = canceledEx.Message,
                };

                // Processing jobs has been cancelled by CancelImportRequestHandler
                await WaitCancelledJobCompletedAsync(jobInfo);
                await SendImportMetricsNotification(JobStatus.Cancelled, jobInfo, inputData, currentResult);
            }
            catch (IntegrationDataStoreException integrationDataStoreEx)
            {
                _logger.LogInformation(integrationDataStoreEx, "Failed to access input files.");

                errorResult = new ImportOrchestratorJobErrorResult()
                {
                    HttpStatusCode = integrationDataStoreEx.StatusCode,
                    ErrorMessage = integrationDataStoreEx.Message,
                };

                await SendImportMetricsNotification(JobStatus.Failed, jobInfo, inputData, currentResult);
            }
            catch (ImportFileEtagNotMatchException eTagEx)
            {
                _logger.LogInformation(eTagEx, "Import file etag not match.");

                errorResult = new ImportOrchestratorJobErrorResult()
                {
                    HttpStatusCode = HttpStatusCode.BadRequest,
                    ErrorMessage = eTagEx.Message,
                };

                await SendImportMetricsNotification(JobStatus.Failed, jobInfo, inputData, currentResult);
            }
            catch (ImportProcessingException processingEx)
            {
                _logger.LogInformation(processingEx, "Failed to process input resources.");

                errorResult = new ImportOrchestratorJobErrorResult()
                {
                    HttpStatusCode = HttpStatusCode.BadRequest,
                    ErrorMessage = processingEx.Message,
                };

                // Cancel other processing jobs
                await CancelProcessingJobsAsync(jobInfo);
                await SendImportMetricsNotification(JobStatus.Failed, jobInfo, inputData, currentResult);
            }
            catch (RetriableJobException ex)
            {
                _logger.LogInformation(ex, "Failed with RetriableJobException.");

                throw;
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "Failed to import data.");

                errorResult = new ImportOrchestratorJobErrorResult()
                {
                    HttpStatusCode = HttpStatusCode.InternalServerError,
                    ErrorMessage = ex.Message,
                };

                // Cancel processing jobs for critical error in orchestrator job
                await CancelProcessingJobsAsync(jobInfo);
                await SendImportMetricsNotification(JobStatus.Failed, jobInfo, inputData, currentResult);
            }

            // Post-process operation cannot be cancelled.
            try
            {
                await _importOrchestratorJobDataStoreOperation.PostprocessAsync(CancellationToken.None);

                _logger.LogInformation("Postprocess Completed");
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "Failed at postprocess step.");

                ImportOrchestratorJobErrorResult postProcessErrorResult = new ImportOrchestratorJobErrorResult()
                {
                    HttpStatusCode = HttpStatusCode.InternalServerError,
                    ErrorMessage = ex.Message,
                };

                throw new RetriableJobException(JsonConvert.SerializeObject(postProcessErrorResult));
            }

            if (errorResult != null)
            {
                throw new JobExecutionException(errorResult.ErrorMessage, errorResult);
            }

            await SendImportMetricsNotification(JobStatus.Completed, jobInfo, inputData, currentResult);
            return JsonConvert.SerializeObject(currentResult);
        }

        private static long CalculateResourceNumberByResourceSize(long blobSizeInBytes, long resourceCountPerBytes)
        {
            return Math.Max((blobSizeInBytes / resourceCountPerBytes) + 1, 10000L);
        }

        private async Task ValidateResourcesAsync(ImportOrchestratorJobInputData inputData, CancellationToken cancellationToken)
        {
            foreach (var input in inputData.Input)
            {
                Dictionary<string, object> properties = await _integrationDataStoreClient.GetPropertiesAsync(input.Url, cancellationToken);
                if (!string.IsNullOrEmpty(input.Etag))
                {
                    if (!input.Etag.Equals(properties[IntegrationDataStoreClientConstants.BlobPropertyETag]))
                    {
                        throw new ImportFileEtagNotMatchException(string.Format("Input file Etag not match. {0}", input.Url));
                    }
                }
            }
        }

        private async Task SendImportMetricsNotification(JobStatus jobStatus, JobInfo jobInfo, ImportOrchestratorJobInputData inputData, ImportOrchestratorJobResult currentResult)
        {
            ImportJobMetricsNotification importJobMetricsNotification = new ImportJobMetricsNotification(
                jobInfo.Id.ToString(),
                jobStatus.ToString(),
                inputData.CreateTime,
                Clock.UtcNow,
                currentResult.TotalSizeInBytes,
                currentResult.SucceedImportCount,
                currentResult.FailedImportCount);

            await _mediator.Publish(importJobMetricsNotification, CancellationToken.None);
        }

        private async Task ExecuteImportProcessingJobAsync(IProgress<string> progress, JobInfo coord, ImportOrchestratorJobInputData coordDefinition, ImportOrchestratorJobResult currentResult, bool isMerge, CancellationToken cancellationToken)
        {
            if (isMerge)
            {
                currentResult.TotalSizeInBytes = 0;
                currentResult.FailedImportCount = 0;
                currentResult.SucceedImportCount = 0;

                // split blobs by size
                var inputs = new List<Models.InputResource>();
                foreach (var input in coordDefinition.Input)
                {
                    var blobLength = (long)(await _integrationDataStoreClient.GetPropertiesAsync(input.Url, cancellationToken))[IntegrationDataStoreClientConstants.BlobPropertyLength];
                    currentResult.TotalSizeInBytes += blobLength;
                    var numberOfStreams = (int)Math.Ceiling((double)blobLength / BytesToRead);
                    numberOfStreams = numberOfStreams == 0 ? 1 : numberOfStreams; // record blob even if it is empty
                    for (var stream = 0; stream < numberOfStreams; stream++)
                    {
                        var newInput = input.Clone();
                        newInput.Offset = stream * BytesToRead;
                        newInput.BytesToRead = BytesToRead;
                        inputs.Add(newInput);
                    }
                }

                var jobIds = await EnqueueProcessingJobsAsync(inputs, coord.GroupId, coordDefinition, currentResult, cancellationToken);
                progress.Report(JsonConvert.SerializeObject(currentResult));

                currentResult.CreatedJobCount = jobIds.Count;

                await WaitCompletion(progress, jobIds, currentResult, cancellationToken);
            }
            else
            {
                currentResult.TotalSizeInBytes = currentResult.TotalSizeInBytes ?? 0;

                foreach (var input in coordDefinition.Input.Skip(currentResult.CreatedJobCount))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException();
                    }

                    while (currentResult.RunningJobIds.Count >= _importConfiguration.MaxRunningProcessingJobCount)
                    {
                        await WaitRunningJobComplete(progress, coord, currentResult, cancellationToken);
                    }

                    (long processingJobId, long endSequenceId, long blobSizeInBytes) = await CreateNewProcessingJobAsync(input, coord, coordDefinition, currentResult, cancellationToken);

                    currentResult.RunningJobIds.Add(processingJobId);
                    currentResult.CurrentSequenceId = endSequenceId;
                    currentResult.TotalSizeInBytes += blobSizeInBytes;
                    currentResult.CreatedJobCount += 1;
                    progress.Report(JsonConvert.SerializeObject(currentResult));
                }

                while (currentResult.RunningJobIds.Count > 0)
                {
                    await WaitRunningJobComplete(progress, coord, currentResult, cancellationToken);
                }
            }
        }

        private async Task WaitCompletion(IProgress<string> progress, IList<long> jobIds, ImportOrchestratorJobResult currentResult, CancellationToken cancellationToken)
        {
            do
            {
                await Task.Delay(TimeSpan.FromSeconds(PollingFrequencyInSeconds), cancellationToken); // there is no sense in checking right away as workers are polling queue on the same interval

                var completedJobIds = new HashSet<long>();
                var jobIdsToCheck = jobIds.Take(20).ToList();
                var jobInfos = new List<JobInfo>();
                try
                {
                    jobInfos.AddRange(await _queueClient.GetJobsByIdsAsync((byte)QueueType.Import, jobIdsToCheck.ToArray(), false, cancellationToken));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to get running jobs.");
                    throw new RetriableJobException(ex.Message, ex);
                }

                foreach (var jobInfo in jobInfos)
                {
                    if (jobInfo.Status != JobStatus.Created && jobInfo.Status != JobStatus.Running)
                    {
                        if (jobInfo.Status == JobStatus.Completed)
                        {
                            var procesingJobResult = JsonConvert.DeserializeObject<ImportProcessingJobResult>(jobInfo.Result);
                            currentResult.SucceedImportCount += procesingJobResult.SucceedCount;
                            currentResult.FailedImportCount += procesingJobResult.FailedCount;
                        }
                        else if (jobInfo.Status == JobStatus.Failed)
                        {
                            var procesingJobResult = JsonConvert.DeserializeObject<ImportProcessingJobErrorResult>(jobInfo.Result);
                            throw new ImportProcessingException(procesingJobResult.Message);
                        }
                        else if (jobInfo.Status == JobStatus.Cancelled)
                        {
                            throw new OperationCanceledException("Import operation cancelled by customer.");
                        }

                        completedJobIds.Add(jobInfo.Id);
                    }
                }

                if (completedJobIds.Count > 0)
                {
                    foreach (var jobId in completedJobIds)
                    {
                        jobIds.Remove(jobId);
                    }

                    progress.Report(JsonConvert.SerializeObject(currentResult));
                }
            }
            while (jobIds.Count > 0);
        }

        private async Task WaitRunningJobComplete(IProgress<string> progress, JobInfo jobInfo, ImportOrchestratorJobResult currentResult, CancellationToken cancellationToken)
        {
            HashSet<long> completedJobIds = new HashSet<long>();
            List<JobInfo> runningJobs = new List<JobInfo>();
            try
            {
                runningJobs.AddRange(await _queueClient.GetJobsByIdsAsync(jobInfo.QueueType, currentResult.RunningJobIds.ToArray(), false, cancellationToken));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get running jobs.");
                throw new RetriableJobException(ex.Message, ex);
            }

            foreach (JobInfo latestJobInfo in runningJobs)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException();
                }

                if (latestJobInfo.Status != JobStatus.Created && latestJobInfo.Status != JobStatus.Running)
                {
                    if (latestJobInfo.Status == JobStatus.Completed)
                    {
                        ImportProcessingJobResult procesingJobResult = JsonConvert.DeserializeObject<ImportProcessingJobResult>(latestJobInfo.Result);
                        currentResult.SucceedImportCount += procesingJobResult.SucceedCount;
                        currentResult.FailedImportCount += procesingJobResult.FailedCount;
                    }
                    else if (latestJobInfo.Status == JobStatus.Failed)
                    {
                        ImportProcessingJobErrorResult procesingJobResult = JsonConvert.DeserializeObject<ImportProcessingJobErrorResult>(latestJobInfo.Result);
                        throw new ImportProcessingException(procesingJobResult.Message);
                    }
                    else if (latestJobInfo.Status == JobStatus.Cancelled)
                    {
                        throw new OperationCanceledException("Import operation cancelled by customer.");
                    }

                    completedJobIds.Add(latestJobInfo.Id);
                }
            }

            if (completedJobIds.Count > 0)
            {
                currentResult.RunningJobIds.ExceptWith(completedJobIds);
                progress.Report(JsonConvert.SerializeObject(currentResult));
            }
            else
            {
                // Only wait if no completed job (optimized for small jobs)
                await Task.Delay(TimeSpan.FromSeconds(PollingFrequencyInSeconds), cancellationToken);
            }
        }

        private async Task<IList<long>> EnqueueProcessingJobsAsync(IEnumerable<Models.InputResource> inputs, long groupId, ImportOrchestratorJobInputData coordDefinition, ImportOrchestratorJobResult currentResult, CancellationToken cancellationToken)
        {
            var definitions = new List<string>();
            foreach (var input in inputs.OrderBy(_ => RandomNumberGenerator.GetInt32((int)1e9)))
            {
                var importJobPayload = new ImportProcessingJobInputData()
                {
                    TypeId = (int)JobType.ImportProcessing,
                    ResourceLocation = input.Url.ToString(),
                    Offset = input.Offset,
                    BytesToRead = input.BytesToRead,
                    UriString = coordDefinition.RequestUri.ToString(),
                    BaseUriString = coordDefinition.BaseUri.ToString(),
                    ResourceType = input.Type,
                    JobId = $"{groupId}", // TODO: remove in stage 2
                };

                definitions.Add(JsonConvert.SerializeObject(importJobPayload));
            }

            try
            {
                var jobIds = (await _queueClient.EnqueueAsync((byte)QueueType.Import, definitions.ToArray(), groupId, false, false, cancellationToken)).Select(_ => _.Id).OrderBy(_ => _).ToList();
                return jobIds;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enqueue job.");
                throw new RetriableJobException(ex.Message, ex);
            }
        }

        private async Task<(long jobId, long endSequenceId, long blobSizeInBytes)> CreateNewProcessingJobAsync(Models.InputResource input, JobInfo jobInfo, ImportOrchestratorJobInputData inputData, ImportOrchestratorJobResult currentResult, CancellationToken cancellationToken)
        {
            Dictionary<string, object> properties = await _integrationDataStoreClient.GetPropertiesAsync(input.Url, cancellationToken);
            long blobSizeInBytes = (long)properties[IntegrationDataStoreClientConstants.BlobPropertyLength];
            long estimatedResourceNumber = CalculateResourceNumberByResourceSize(blobSizeInBytes, DefaultResourceSizePerByte);
            long beginSequenceId = currentResult.CurrentSequenceId;
            long endSequenceId = beginSequenceId + estimatedResourceNumber;

            ImportProcessingJobInputData importJobPayload = new ImportProcessingJobInputData()
            {
                TypeId = (int)JobType.ImportProcessing,
                ResourceLocation = input.Url.ToString(),
                UriString = inputData.RequestUri.ToString(),
                BaseUriString = inputData.BaseUri.ToString(),
                ResourceType = input.Type,
                BeginSequenceId = beginSequenceId,
                EndSequenceId = endSequenceId,
                JobId = $"{jobInfo.GroupId}_{beginSequenceId}",
            };

            string[] definitions = new string[] { JsonConvert.SerializeObject(importJobPayload) };

            try
            {
                JobInfo jobInfoFromServer = (await _queueClient.EnqueueAsync(jobInfo.QueueType, definitions, jobInfo.GroupId, false, false, cancellationToken))[0];

                return (jobInfoFromServer.Id, endSequenceId, blobSizeInBytes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enqueue job.");
                throw new RetriableJobException(ex.Message, ex);
            }
        }

        private async Task CancelProcessingJobsAsync(JobInfo jobInfo)
        {
            try
            {
                await _queueClient.CancelJobByGroupIdAsync(jobInfo.QueueType, jobInfo.GroupId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "failed to cancel job {GroupId}", jobInfo.GroupId);
            }

            await WaitCancelledJobCompletedAsync(jobInfo);
        }

        private async Task WaitCancelledJobCompletedAsync(JobInfo jobInfo)
        {
            while (true)
            {
                try
                {
                    IEnumerable<JobInfo> jobInfos = await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Import, jobInfo.GroupId, false, CancellationToken.None);

                    if (jobInfos.All(t => (t.Status != JobStatus.Created && t.Status != JobStatus.Running) || !t.CancelRequested || t.Id == jobInfo.Id))
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "failed to get jobs by groupId {GroupId}", jobInfo.GroupId);
                    throw new RetriableJobException(ex.Message, ex);
                }

                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }
    }
}
