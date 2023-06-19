// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Operations.Import.Models;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;
using JobStatus = Microsoft.Health.JobManagement.JobStatus;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Import
{
    [JobTypeId((int)JobType.ImportOrchestrator)]
    public class ImportOrchestratorJob : IJob
    {
        public const int BytesToRead = 10000 * 1000; // each job should handle about 10000 resources. with about 1000 bytes per resource

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

            PollingPeriodSec = _importConfiguration.PollingFrequencyInSeconds;
        }

        public int PollingPeriodSec { get; set; }

        public async Task<string> ExecuteAsync(JobInfo jobInfo, IProgress<string> progress, CancellationToken cancellationToken)
        {
            ImportOrchestratorJobDefinition inputData = JsonConvert.DeserializeObject<ImportOrchestratorJobDefinition>(jobInfo.Definition);
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
                    progress.Report(JsonConvert.SerializeObject(currentResult));

                    _logger.LogInformation("Preprocess Completed");
                }

                if (currentResult.Progress == ImportOrchestratorJobProgress.PreprocessCompleted)
                {
                    await ExecuteImportProcessingJobAsync(progress, jobInfo, inputData, currentResult, cancellationToken);
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
                await SendImportMetricsNotification(JobStatus.Cancelled, jobInfo, currentResult, inputData.ImportMode);
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
                await SendImportMetricsNotification(JobStatus.Cancelled, jobInfo, currentResult, inputData.ImportMode);
            }
            catch (IntegrationDataStoreException integrationDataStoreEx)
            {
                _logger.LogInformation(integrationDataStoreEx, "Failed to access input files.");

                errorResult = new ImportOrchestratorJobErrorResult()
                {
                    HttpStatusCode = integrationDataStoreEx.StatusCode,
                    ErrorMessage = integrationDataStoreEx.Message,
                };

                await SendImportMetricsNotification(JobStatus.Failed, jobInfo, currentResult, inputData.ImportMode);
            }
            catch (ImportFileEtagNotMatchException eTagEx)
            {
                _logger.LogInformation(eTagEx, "Import file etag not match.");

                errorResult = new ImportOrchestratorJobErrorResult()
                {
                    HttpStatusCode = HttpStatusCode.BadRequest,
                    ErrorMessage = eTagEx.Message,
                };

                await SendImportMetricsNotification(JobStatus.Failed, jobInfo, currentResult, inputData.ImportMode);
            }
            catch (ImportProcessingException processingEx)
            {
                _logger.LogInformation(processingEx, "Failed to process input resources.");

                errorResult = new ImportOrchestratorJobErrorResult()
                {
                    HttpStatusCode = HttpStatusCode.BadRequest,
                    ErrorMessage = processingEx.Message,
                    ErrorDetails = processingEx.ToString(),
                };

                // Cancel other processing jobs
                await CancelProcessingJobsAsync(jobInfo);
                await SendImportMetricsNotification(JobStatus.Failed, jobInfo, currentResult, inputData.ImportMode);
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
                    ErrorDetails = ex.ToString(),
                };

                // Cancel processing jobs for critical error in orchestrator job
                await CancelProcessingJobsAsync(jobInfo);
                await SendImportMetricsNotification(JobStatus.Failed, jobInfo, currentResult, inputData.ImportMode);
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
                    ErrorDetails = ex.ToString(),
                };

                throw new RetriableJobException(JsonConvert.SerializeObject(postProcessErrorResult));
            }

            if (errorResult != null)
            {
                throw new JobExecutionException(errorResult.ErrorMessage, errorResult);
            }

            await SendImportMetricsNotification(JobStatus.Completed, jobInfo, currentResult, inputData.ImportMode);
            return JsonConvert.SerializeObject(currentResult);
        }

        private async Task ValidateResourcesAsync(ImportOrchestratorJobDefinition inputData, CancellationToken cancellationToken)
        {
            await Parallel.ForEachAsync(inputData.Input, new ParallelOptions { MaxDegreeOfParallelism = 16 }, async (input, cancel) =>
            {
                Dictionary<string, object> properties = await _integrationDataStoreClient.GetPropertiesAsync(input.Url, cancellationToken);
                if (!string.IsNullOrEmpty(input.Etag))
                {
                    if (!input.Etag.Equals(properties[IntegrationDataStoreClientConstants.BlobPropertyETag]))
                    {
                        throw new ImportFileEtagNotMatchException(string.Format("Input file Etag not match. {0}", input.Url));
                    }
                }
            });
        }

        private async Task SendImportMetricsNotification(JobStatus jobStatus, JobInfo jobInfo, ImportOrchestratorJobResult currentResult, ImportMode importMode)
        {
            var importJobMetricsNotification = new ImportJobMetricsNotification(
                jobInfo.Id.ToString(),
                jobStatus.ToString(),
                jobInfo.CreateDate,
                Clock.UtcNow,
                currentResult.TotalBytes,
                currentResult.SucceededResources,
                currentResult.FailedResources,
                importMode);

            await _mediator.Publish(importJobMetricsNotification, CancellationToken.None);
        }

        private async Task ExecuteImportProcessingJobAsync(IProgress<string> progress, JobInfo coord, ImportOrchestratorJobDefinition coordDefinition, ImportOrchestratorJobResult currentResult, CancellationToken cancellationToken)
        {
            currentResult.TotalBytes = 0;
            currentResult.FailedResources = 0;
            currentResult.SucceededResources = 0;

            // split blobs by size
            var inputs = new List<InputResource>();
            await Parallel.ForEachAsync(coordDefinition.Input, new ParallelOptions { MaxDegreeOfParallelism = 16 }, async (input, cancel) =>
            {
                var blobLength = (long)(await _integrationDataStoreClient.GetPropertiesAsync(input.Url, cancellationToken))[IntegrationDataStoreClientConstants.BlobPropertyLength];
                currentResult.TotalBytes += blobLength;
                var numberOfStreams = (int)Math.Ceiling((double)blobLength / BytesToRead);
                numberOfStreams = numberOfStreams == 0 ? 1 : numberOfStreams; // record blob even if it is empty
                for (var stream = 0; stream < numberOfStreams; stream++)
                {
                    var newInput = input.Clone();
                    newInput.Offset = (long)stream * BytesToRead; // make sure that arithmetic on long is used
                    newInput.BytesToRead = BytesToRead;
                    lock (inputs)
                    {
                        inputs.Add(newInput);
                    }
                }
            });

            var jobIds = await EnqueueProcessingJobsAsync(inputs, coord.GroupId, coordDefinition, currentResult, cancellationToken);
            progress.Report(JsonConvert.SerializeObject(currentResult));

            currentResult.CreatedJobs = jobIds.Count;

            await WaitCompletion(progress, jobIds, currentResult, cancellationToken);
        }

        private async Task WaitCompletion(IProgress<string> progress, IList<long> jobIds, ImportOrchestratorJobResult currentResult, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(PollingPeriodSec), cancellationToken); // there is no sense in checking right away as workers are polling queue on the same interval

            do
            {
                var completedJobIds = new HashSet<long>();
                var jobIdsToCheck = jobIds.Take(20).ToList();
                var jobInfos = new List<JobInfo>();
                double duration;
                try
                {
                    var start = Stopwatch.StartNew();
                    jobInfos.AddRange(await _queueClient.GetJobsByIdsAsync((byte)QueueType.Import, jobIdsToCheck.ToArray(), false, cancellationToken));
                    duration = start.Elapsed.TotalSeconds;
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
                            currentResult.SucceededResources += procesingJobResult.SucceededResources == 0 ? procesingJobResult.SucceedCount : procesingJobResult.SucceededResources;
                            currentResult.FailedResources += procesingJobResult.FailedResources == 0 ? procesingJobResult.FailedCount : procesingJobResult.FailedResources;
                            currentResult.ProcessedBytes += procesingJobResult.ProcessedBytes;
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

                    currentResult.CompletedJobs += completedJobIds.Count;
                    progress.Report(JsonConvert.SerializeObject(currentResult));

                    await Task.Delay(TimeSpan.FromSeconds(duration), cancellationToken); // throttle to avoid high database utilization.
                }
                else
                {
                    await Task.Delay(TimeSpan.FromSeconds(PollingPeriodSec), cancellationToken);
                }
            }
            while (jobIds.Count > 0);
        }

        private async Task<IList<long>> EnqueueProcessingJobsAsync(IEnumerable<InputResource> inputs, long groupId, ImportOrchestratorJobDefinition coordDefinition, ImportOrchestratorJobResult currentResult, CancellationToken cancellationToken)
        {
            var definitions = new List<string>();
            foreach (var input in inputs.OrderBy(_ => RandomNumberGenerator.GetInt32((int)1e9)))
            {
                var importJobPayload = new ImportProcessingJobDefinition()
                {
                    TypeId = (int)JobType.ImportProcessing,
                    ResourceLocation = input.Url.ToString(),
                    Offset = input.Offset,
                    BytesToRead = input.BytesToRead,
                    UriString = coordDefinition.RequestUri.ToString(),
                    BaseUriString = coordDefinition.BaseUri.ToString(),
                    ResourceType = input.Type,
                    GroupId = groupId,
                    ImportMode = coordDefinition.ImportMode,
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
