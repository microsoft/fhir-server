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
using MediatR;
using Microsoft.Extensions.Logging;
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
    public class ImportOrchestratorJob : IJob
    {
        public const short ImportOrchestratorTypeId = 2;

        private const int DefaultPollingFrequencyInSeconds = 60;
        private const long DefaultResourceSizePerByte = 64;

        private readonly IMediator _mediator;
        private ImportOrchestratorJobInputData _orchestratorInputData;
        private RequestContextAccessor<IFhirRequestContext> _contextAccessor;
        private ImportOrchestratorJobResult _orchestratorJobResult;
        private IImportOrchestratorJobDataStoreOperation _importOrchestratorJobDataStoreOperation;
        private IQueueClient _queueClient;
        private JobInfo _jobInfo;
        private ImportTaskConfiguration _importConfiguration;
        private ILogger<ImportOrchestratorJob> _logger;
        private IIntegrationDataStoreClient _integrationDataStoreClient;

        public ImportOrchestratorJob(
            IMediator mediator,
            ImportOrchestratorJobInputData orchestratorInputData,
            ImportOrchestratorJobResult orchrestratorJobResult,
            RequestContextAccessor<IFhirRequestContext> contextAccessor,
            IImportOrchestratorJobDataStoreOperation importOrchestratorJobDataStoreOperation,
            IIntegrationDataStoreClient integrationDataStoreClient,
            IQueueClient queueClient,
            JobInfo jobInfo,
            ImportTaskConfiguration importConfiguration,
            ILoggerFactory loggerFactory)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(orchestratorInputData, nameof(orchestratorInputData));
            EnsureArg.IsNotNull(orchrestratorJobResult, nameof(orchrestratorJobResult));
            EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));
            EnsureArg.IsNotNull(importOrchestratorJobDataStoreOperation, nameof(importOrchestratorJobDataStoreOperation));
            EnsureArg.IsNotNull(integrationDataStoreClient, nameof(integrationDataStoreClient));
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));
            EnsureArg.IsNotNull(importConfiguration, nameof(importConfiguration));
            EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory));

            _mediator = mediator;
            _orchestratorInputData = orchestratorInputData;
            _orchestratorJobResult = orchrestratorJobResult;
            _contextAccessor = contextAccessor;
            _importOrchestratorJobDataStoreOperation = importOrchestratorJobDataStoreOperation;
            _integrationDataStoreClient = integrationDataStoreClient;
            _queueClient = queueClient;
            _jobInfo = jobInfo;
            _importConfiguration = importConfiguration;
            _logger = loggerFactory.CreateLogger<ImportOrchestratorJob>();

            PollingFrequencyInSeconds = _importConfiguration.PollingFrequencyInSeconds;
        }

        public string RunId { get; set; }

        public int PollingFrequencyInSeconds { get; set; }

        public async Task<string> ExecuteAsync(IProgress<string> progress, CancellationToken cancellationToken)
        {
            var fhirRequestContext = new FhirRequestContext(
                    method: "Import",
                    uriString: _orchestratorInputData.RequestUri.ToString(),
                    baseUriString: _orchestratorInputData.BaseUri.ToString(),
                    correlationId: _jobInfo.Id.ToString(),
                    requestHeaders: new Dictionary<string, StringValues>(),
                    responseHeaders: new Dictionary<string, StringValues>())
            {
                IsBackgroundTask = true,
            };

            _contextAccessor.RequestContext = fhirRequestContext;

            _orchestratorJobResult.Request = _orchestratorInputData.RequestUri.ToString();
            _orchestratorJobResult.TransactionTime = _orchestratorInputData.CreateTime;

            ImportOrchestratorJobErrorResult errorResult = null;

            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException();
                }

                if (_orchestratorJobResult.Progress == ImportOrchestratorJobProgress.Initialized)
                {
                    await ValidateResourcesAsync(cancellationToken);

                    _orchestratorJobResult.Progress = ImportOrchestratorJobProgress.InputResourcesValidated;
                    progress.Report(JsonConvert.SerializeObject(_orchestratorJobResult));

                    _logger.LogInformation("Input Resources Validated");
                }

                if (_orchestratorJobResult.Progress == ImportOrchestratorJobProgress.InputResourcesValidated)
                {
                    await _importOrchestratorJobDataStoreOperation.PreprocessAsync(cancellationToken);

                    _orchestratorJobResult.Progress = ImportOrchestratorJobProgress.PreprocessCompleted;
                    _orchestratorJobResult.CurrentSequenceId = _orchestratorInputData.StartSequenceId;
                    progress.Report(JsonConvert.SerializeObject(_orchestratorJobResult));

                    _logger.LogInformation("Preprocess Completed");
                }

                if (_orchestratorJobResult.Progress == ImportOrchestratorJobProgress.PreprocessCompleted)
                {
                    await ExecuteImportProcessingJobAsync(progress, cancellationToken);
                    _orchestratorJobResult.Progress = ImportOrchestratorJobProgress.SubJobsCompleted;
                    progress.Report(JsonConvert.SerializeObject(_orchestratorJobResult));

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
                await WaitCancelledJobCompletedAsync();
                await SendImportMetricsNotification(JobStatus.Cancelled);
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
                await WaitCancelledJobCompletedAsync();
                await SendImportMetricsNotification(JobStatus.Cancelled);
            }
            catch (IntegrationDataStoreException integrationDataStoreEx)
            {
                _logger.LogInformation(integrationDataStoreEx, "Failed to access input files.");

                errorResult = new ImportOrchestratorJobErrorResult()
                {
                    HttpStatusCode = integrationDataStoreEx.StatusCode,
                    ErrorMessage = integrationDataStoreEx.Message,
                };

                await SendImportMetricsNotification(JobStatus.Failed);
            }
            catch (ImportFileEtagNotMatchException eTagEx)
            {
                _logger.LogInformation(eTagEx, "Import file etag not match.");

                errorResult = new ImportOrchestratorJobErrorResult()
                {
                    HttpStatusCode = HttpStatusCode.BadRequest,
                    ErrorMessage = eTagEx.Message,
                };

                await SendImportMetricsNotification(JobStatus.Failed);
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
                await CancelProcessingJobsAsync();
                await SendImportMetricsNotification(JobStatus.Failed);
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
                await CancelProcessingJobsAsync();
                await SendImportMetricsNotification(JobStatus.Failed);
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

            await SendImportMetricsNotification(JobStatus.Completed);
            return JsonConvert.SerializeObject(_orchestratorJobResult);
        }

        private static long CalculateResourceNumberByResourceSize(long blobSizeInBytes, long resourceCountPerBytes)
        {
            return Math.Max((blobSizeInBytes / resourceCountPerBytes) + 1, 10000L);
        }

        private async Task ValidateResourcesAsync(CancellationToken cancellationToken)
        {
            foreach (var input in _orchestratorInputData.Input)
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

        private async Task SendImportMetricsNotification(JobStatus jobStatus)
        {
            ImportJobMetricsNotification importJobMetricsNotification = new ImportJobMetricsNotification(
                _jobInfo.Id.ToString(),
                jobStatus.ToString(),
                _orchestratorInputData.CreateTime,
                Clock.UtcNow,
                _orchestratorJobResult.TotalSizeInBytes,
                _orchestratorJobResult.SucceedImportCount,
                _orchestratorJobResult.FailedImportCount);

            await _mediator.Publish(importJobMetricsNotification, CancellationToken.None);
        }

        private async Task ExecuteImportProcessingJobAsync(IProgress<string> progress, CancellationToken cancellationToken)
        {
            _orchestratorJobResult.TotalSizeInBytes = _orchestratorJobResult.TotalSizeInBytes ?? 0;

            foreach (var input in _orchestratorInputData.Input.Skip(_orchestratorJobResult.CreatedJobCount))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException();
                }

                while (_orchestratorJobResult.RunningJobIds.Count >= _importConfiguration.MaxRunningProcessingJobCount)
                {
                    await WaitRunningJobComplete(progress, cancellationToken);
                }

                (long processingJobId, long endSequenceId, long blobSizeInBytes) = await CreateNewProcessingJobAsync(input, cancellationToken);

                _orchestratorJobResult.RunningJobIds.Add(processingJobId);
                _orchestratorJobResult.CurrentSequenceId = endSequenceId;
                _orchestratorJobResult.TotalSizeInBytes += blobSizeInBytes;
                _orchestratorJobResult.CreatedJobCount += 1;
                progress.Report(JsonConvert.SerializeObject(_orchestratorJobResult));
            }

            while (_orchestratorJobResult.RunningJobIds.Count > 0)
            {
                await WaitRunningJobComplete(progress, cancellationToken);
            }
        }

        private async Task WaitRunningJobComplete(IProgress<string> progress, CancellationToken cancellationToken)
        {
            HashSet<long> completedJobIds = new HashSet<long>();
            List<JobInfo> runningJobs = new List<JobInfo>();
            try
            {
                runningJobs.AddRange(await _queueClient.GetJobsByIdsAsync(_jobInfo.QueueType, _orchestratorJobResult.RunningJobIds.ToArray(), false, cancellationToken));
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
                        _orchestratorJobResult.SucceedImportCount += procesingJobResult.SucceedCount;
                        _orchestratorJobResult.FailedImportCount += procesingJobResult.FailedCount;
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
                _orchestratorJobResult.RunningJobIds.ExceptWith(completedJobIds);
                progress.Report(JsonConvert.SerializeObject(_orchestratorJobResult));
            }
            else
            {
                // Only wait if no completed job (optimized for small jobs)
                await Task.Delay(TimeSpan.FromSeconds(PollingFrequencyInSeconds), cancellationToken);
            }
        }

        private async Task<(long jobId, long endSequenceId, long blobSizeInBytes)> CreateNewProcessingJobAsync(Models.InputResource input, CancellationToken cancellationToken)
        {
            Dictionary<string, object> properties = await _integrationDataStoreClient.GetPropertiesAsync(input.Url, cancellationToken);
            long blobSizeInBytes = (long)properties[IntegrationDataStoreClientConstants.BlobPropertyLength];
            long estimatedResourceNumber = CalculateResourceNumberByResourceSize(blobSizeInBytes, DefaultResourceSizePerByte);
            long beginSequenceId = _orchestratorJobResult.CurrentSequenceId;
            long endSequenceId = beginSequenceId + estimatedResourceNumber;

            ImportProcessingJobInputData importJobPayload = new ImportProcessingJobInputData()
            {
                TypeId = ImportProcessingJob.ImportProcessingJobTypeId,
                ResourceLocation = input.Url.ToString(),
                UriString = _orchestratorInputData.RequestUri.ToString(),
                BaseUriString = _orchestratorInputData.BaseUri.ToString(),
                ResourceType = input.Type,
                BeginSequenceId = beginSequenceId,
                EndSequenceId = endSequenceId,
                JobId = $"{_jobInfo.GroupId}_{beginSequenceId}",
            };

            string[] definitions = new string[] { JsonConvert.SerializeObject(importJobPayload) };

            try
            {
                JobInfo jobInfoFromServer = (await _queueClient.EnqueueAsync(_jobInfo.QueueType, definitions, _jobInfo.GroupId, false, false, cancellationToken)).First();

                return (jobInfoFromServer.Id, endSequenceId, blobSizeInBytes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enqueue job.");
                throw new RetriableJobException(ex.Message, ex);
            }
        }

        private async Task CancelProcessingJobsAsync()
        {
            try
            {
                await _queueClient.CancelJobByGroupIdAsync(_jobInfo.QueueType, _jobInfo.GroupId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "failed to cancel job {GroupId}", _jobInfo.GroupId);
            }

            await WaitCancelledJobCompletedAsync();
        }

        private async Task WaitCancelledJobCompletedAsync()
        {
            while (true)
            {
                try
                {
                    IEnumerable<JobInfo> jobInfos = await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Import, _jobInfo.GroupId, false, CancellationToken.None);

                    if (jobInfos.All(t => (t.Status != JobStatus.Created && t.Status != JobStatus.Running) || !t.CancelRequested || t.Id == _jobInfo.Id))
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "failed to get jobs by groupId {GroupId}", _jobInfo.GroupId);
                    throw new RetriableJobException(ex.Message, ex);
                }

                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }
    }
}
