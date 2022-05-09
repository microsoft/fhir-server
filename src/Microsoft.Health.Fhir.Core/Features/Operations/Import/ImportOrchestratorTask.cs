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
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.TaskManagement;
using Newtonsoft.Json;
using TaskStatus = Microsoft.Health.TaskManagement.TaskStatus;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportOrchestratorTask : ITask
    {
        public const short ImportOrchestratorTaskId = 2;

        private const int DefaultPollingFrequencyInSeconds = 3;
        private const long DefaultResourceSizePerByte = 64;

        private readonly IMediator _mediator;
        private ImportOrchestratorTaskInputData _orchestratorInputData;
        private RequestContextAccessor<IFhirRequestContext> _contextAccessor;
        private ImportTaskResult _importTaskResult;
        private ISequenceIdGenerator<long> _sequenceIdGenerator;
        private IImportOrchestratorTaskDataStoreOperation _importOrchestratorTaskDataStoreOperation;
        private IQueueClient _queueClient;
        private TaskInfo _taskInfo;
        private ILogger<ImportOrchestratorTask> _logger;
        private IIntegrationDataStoreClient _integrationDataStoreClient;

        public ImportOrchestratorTask(
            IMediator mediator,
            ImportOrchestratorTaskInputData orchestratorInputData,
            ImportTaskResult importTaskResult,
            ISequenceIdGenerator<long> sequenceIdGenerator,
            RequestContextAccessor<IFhirRequestContext> contextAccessor,
            IImportOrchestratorTaskDataStoreOperation importOrchestratorTaskDataStoreOperation,
            IIntegrationDataStoreClient integrationDataStoreClient,
            IQueueClient queueClient,
            TaskInfo taskInfo,
            ILoggerFactory loggerFactory)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(orchestratorInputData, nameof(orchestratorInputData));
            EnsureArg.IsNotNull(importTaskResult, nameof(importTaskResult));
            EnsureArg.IsNotNull(sequenceIdGenerator, nameof(sequenceIdGenerator));
            EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));
            EnsureArg.IsNotNull(importOrchestratorTaskDataStoreOperation, nameof(importOrchestratorTaskDataStoreOperation));
            EnsureArg.IsNotNull(integrationDataStoreClient, nameof(integrationDataStoreClient));
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            EnsureArg.IsNotNull(taskInfo, nameof(taskInfo));
            EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory));

            _mediator = mediator;
            _orchestratorInputData = orchestratorInputData;
            _importTaskResult = importTaskResult;
            _sequenceIdGenerator = sequenceIdGenerator;
            _contextAccessor = contextAccessor;
            _importOrchestratorTaskDataStoreOperation = importOrchestratorTaskDataStoreOperation;
            _integrationDataStoreClient = integrationDataStoreClient;
            _queueClient = queueClient;
            _taskInfo = taskInfo;
            _logger = loggerFactory.CreateLogger<ImportOrchestratorTask>();
        }

        public string RunId { get; set; }

        public int PollingFrequencyInSeconds { get; set; } = DefaultPollingFrequencyInSeconds;

        public async Task<string> ExecuteAsync(IProgress<string> progress, CancellationToken cancellationToken)
        {
            var fhirRequestContext = new FhirRequestContext(
                    method: "Import",
                    uriString: _orchestratorInputData.RequestUri.ToString(),
                    baseUriString: _orchestratorInputData.BaseUri.ToString(),
                    correlationId: _orchestratorInputData.TaskId,
                    requestHeaders: new Dictionary<string, StringValues>(),
                    responseHeaders: new Dictionary<string, StringValues>())
            {
                IsBackgroundTask = true,
            };

            _contextAccessor.RequestContext = fhirRequestContext;

            _importTaskResult.Request = _orchestratorInputData.RequestUri.ToString();
            _importTaskResult.TransactionTime = _orchestratorInputData.TaskCreateTime;

            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException();
                }

                if (_importTaskResult.Progress == ImportOrchestratorTaskProgress.Initialized)
                {
                    await ValidateResourcesAsync(cancellationToken);

                    _importTaskResult.Progress = ImportOrchestratorTaskProgress.InputResourcesValidated;
                    progress.Report(JsonConvert.SerializeObject(_importTaskResult));

                    _logger.LogInformation("Input Resources Validated");
                }

                if (_importTaskResult.Progress == ImportOrchestratorTaskProgress.InputResourcesValidated)
                {
                    await _importOrchestratorTaskDataStoreOperation.PreprocessAsync(cancellationToken);

                    _importTaskResult.Progress = ImportOrchestratorTaskProgress.PreprocessCompleted;
                    _importTaskResult.CurrentSequenceId = _sequenceIdGenerator.GetCurrentSequenceId();
                    progress.Report(JsonConvert.SerializeObject(_importTaskResult));

                    _logger.LogInformation("Preprocess Completed");
                }

                if (_importTaskResult.Progress == ImportOrchestratorTaskProgress.PreprocessCompleted)
                {
                    await ExecuteImportProcessingTaskAsync(progress, cancellationToken);
                    _importTaskResult.Progress = ImportOrchestratorTaskProgress.SubTasksCompleted;
                    progress.Report(JsonConvert.SerializeObject(_importTaskResult));

                    _logger.LogInformation("SubTasks Completed");
                }
            }
            catch (TaskCanceledException taskCanceledEx)
            {
                _logger.LogInformation(taskCanceledEx, "Import task canceled. {0}", taskCanceledEx.Message);

                await CancelProcessingTasksAsync();
                await SendImportMetricsNotification(TaskResult.Canceled);

                throw;
            }
            catch (OperationCanceledException canceledEx)
            {
                _logger.LogInformation(canceledEx, "Import task canceled. {0}", canceledEx.Message);

                await CancelProcessingTasksAsync();
                await SendImportMetricsNotification(TaskResult.Canceled);

                throw;
            }
            catch (IntegrationDataStoreException integrationDataStoreEx)
            {
                _logger.LogInformation(integrationDataStoreEx, "Failed to access input files.");

                ImportTaskErrorResult errorResult = new ImportTaskErrorResult()
                {
                    HttpStatusCode = integrationDataStoreEx.StatusCode,
                    ErrorMessage = integrationDataStoreEx.Message,
                };

                await SendImportMetricsNotification(TaskResult.Fail);
                throw new TaskExecutionException(integrationDataStoreEx.Message, errorResult, integrationDataStoreEx);
            }
            catch (ImportFileEtagNotMatchException eTagEx)
            {
                _logger.LogInformation(eTagEx, "Import file etag not match.");

                ImportTaskErrorResult errorResult = new ImportTaskErrorResult()
                {
                    HttpStatusCode = HttpStatusCode.BadRequest,
                    ErrorMessage = eTagEx.Message,
                };

                await SendImportMetricsNotification(TaskResult.Fail);
                throw new TaskExecutionException(eTagEx.Message, errorResult, eTagEx);
            }
            catch (ImportProcessingException processingEx)
            {
                _logger.LogInformation(processingEx, "Failed to process input resources.");

                ImportTaskErrorResult errorResult = new ImportTaskErrorResult()
                {
                    HttpStatusCode = HttpStatusCode.BadRequest,
                    ErrorMessage = processingEx.Message,
                };

                await SendImportMetricsNotification(TaskResult.Fail);
                throw new TaskExecutionException(processingEx.Message, errorResult, processingEx);
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "Failed to import data.");

                ImportTaskErrorResult errorResult = new ImportTaskErrorResult()
                {
                    HttpStatusCode = HttpStatusCode.InternalServerError,
                    ErrorMessage = ex.Message,
                };

                await SendImportMetricsNotification(TaskResult.Fail);
                throw new TaskExecutionException(ex.Message, errorResult, ex);
            }

            if (_importTaskResult.Progress > ImportOrchestratorTaskProgress.InputResourcesValidated)
            {
                // Post-process operation cannot be cancelled.
                try
                {
                    await _importOrchestratorTaskDataStoreOperation.PostprocessAsync(CancellationToken.None);

                    _logger.LogInformation("Postprocess Completed");
                }
                catch (Exception ex)
                {
                    _logger.LogInformation(ex, "Failed at postprocess step.");

                    ImportTaskErrorResult postProcessErrorResult = new ImportTaskErrorResult()
                    {
                        HttpStatusCode = HttpStatusCode.InternalServerError,
                        ErrorMessage = ex.Message,
                    };

                    await SendImportMetricsNotification(TaskResult.Fail);

                    throw new RetriableTaskException(JsonConvert.SerializeObject(postProcessErrorResult));
                }
            }

            await SendImportMetricsNotification(TaskResult.Success);

            return JsonConvert.SerializeObject(_importTaskResult);
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

        private async Task SendImportMetricsNotification(TaskResult taskResult)
        {
            ImportTaskMetricsNotification importTaskMetricsNotification = new ImportTaskMetricsNotification(
                _orchestratorInputData.TaskId,
                taskResult.ToString(),
                _orchestratorInputData.TaskCreateTime,
                Clock.UtcNow,
                _importTaskResult.TotalSizeInBytes,
                _importTaskResult.SucceedImportCount,
                _importTaskResult.FailedImportCount);

            await _mediator.Publish(importTaskMetricsNotification, CancellationToken.None);
        }

        private async Task ExecuteImportProcessingTaskAsync(IProgress<string> progress, CancellationToken cancellationToken)
        {
            _importTaskResult.TotalSizeInBytes = _importTaskResult.TotalSizeInBytes ?? 0;

            foreach (var input in _orchestratorInputData.Input.Skip(_importTaskResult.CreatedTaskCount))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException();
                }

                while (_importTaskResult.RunningTaskIds.Count >= _orchestratorInputData.MaxConcurrentProcessingTaskCount)
                {
                    await WaitRunningTaskComplete(progress, cancellationToken);
                }

                (long processingTaskId, long endSequenceId, long blobSizeInBytes) = await CreateNewProcessingTaskAsync(input, cancellationToken);

                _importTaskResult.RunningTaskIds.Add(processingTaskId);
                _importTaskResult.CurrentSequenceId = endSequenceId;
                _importTaskResult.TotalSizeInBytes += blobSizeInBytes;
                _importTaskResult.CreatedTaskCount += 1;
                progress.Report(JsonConvert.SerializeObject(_importTaskResult));
            }

            while (_importTaskResult.RunningTaskIds.Count > 0)
            {
                await WaitRunningTaskComplete(progress, cancellationToken);
            }
        }

        private async Task WaitRunningTaskComplete(IProgress<string> progress, CancellationToken cancellationToken)
        {
            HashSet<long> completedTaskIds = new HashSet<long>();
            foreach (long taskId in _importTaskResult.RunningTaskIds)
            {
                TaskInfo latestTaskInfo = await _queueClient.GetTaskByIdAsync(_taskInfo.QueueType, taskId, false, cancellationToken);

                if (latestTaskInfo.Status == TaskStatus.Completed)
                {
                    CheckTaskResult(latestTaskInfo);
                    completedTaskIds.Add(taskId);
                }
            }

            if (completedTaskIds.Count > 0)
            {
                _importTaskResult.RunningTaskIds.RemoveAll(id => completedTaskIds.Contains(id));
                progress.Report(JsonConvert.SerializeObject(_importTaskResult));
            }

            await Task.Delay(TimeSpan.FromSeconds(PollingFrequencyInSeconds), cancellationToken);
        }

        private async Task<(long taskId, long endSequenceId, long blobSizeInBytes)> CreateNewProcessingTaskAsync(Models.InputResource input, CancellationToken cancellationToken)
        {
            Dictionary<string, object> properties = await _integrationDataStoreClient.GetPropertiesAsync(input.Url, cancellationToken);
            long blobSizeInBytes = (long)properties[IntegrationDataStoreClientConstants.BlobPropertyLength];
            long estimatedResourceNumber = CalculateResourceNumberByResourceSize(blobSizeInBytes, DefaultResourceSizePerByte);
            long beginSequenceId = _importTaskResult.CurrentSequenceId;
            long endSequenceId = beginSequenceId + estimatedResourceNumber;

            ImportProcessingTaskInputData importTaskPayload = new ImportProcessingTaskInputData()
            {
                ResourceLocation = input.Url.ToString(),
                UriString = _orchestratorInputData.RequestUri.ToString(),
                BaseUriString = _orchestratorInputData.BaseUri.ToString(),
                ResourceType = input.Type,
                BeginSequenceId = beginSequenceId,
                EndSequenceId = endSequenceId,
            };

            string[] definitions = new string[] { JsonConvert.SerializeObject(importTaskPayload) };

            TaskInfo taskInfoFromServer = (await _queueClient.EnqueueAsync(_taskInfo.QueueType, definitions, _taskInfo.GroupId, false, cancellationToken)).First();

            return (taskInfoFromServer.Id, endSequenceId, blobSizeInBytes);
        }

        private void CheckTaskResult(TaskInfo completeTaskInfo)
        {
            TaskResultData taskResultData = JsonConvert.DeserializeObject<TaskResultData>(completeTaskInfo.Result);
            if (taskResultData.Result == TaskResult.Fail)
            {
                throw new ImportProcessingException(string.Format("Failed to process file: {0}", taskResultData));
            }

            if (taskResultData.Result == TaskResult.Canceled)
            {
                throw new OperationCanceledException(taskResultData.ResultData);
            }

            if (taskResultData.Result == TaskResult.Success)
            {
                ImportProcessingTaskResult procesingTaskResult = JsonConvert.DeserializeObject<ImportProcessingTaskResult>(taskResultData.ResultData);
                _importTaskResult.SucceedImportCount += procesingTaskResult.SucceedCount;
                _importTaskResult.FailedImportCount += procesingTaskResult.FailedCount;
            }
        }

        // Should deprecate after schema upgrade
        private async Task<Dictionary<Uri, TaskInfo>> GenerateSubTaskRecordsAsync(CancellationToken cancellationToken)
        {
            Dictionary<Uri, TaskInfo> result = new Dictionary<Uri, TaskInfo>();

            long beginSequenceId = _sequenceIdGenerator.GetCurrentSequenceId();
            long totalSizeInBytes = 0;

            foreach (var input in _orchestratorInputData.Input)
            {
                string taskId = Guid.NewGuid().ToString("N");

                Dictionary<string, object> properties = await _integrationDataStoreClient.GetPropertiesAsync(input.Url, cancellationToken);
                long blobSizeInBytes = (long)properties[IntegrationDataStoreClientConstants.BlobPropertyLength];
                long estimatedResourceNumber = CalculateResourceNumberByResourceSize(blobSizeInBytes, DefaultResourceSizePerByte);
                long endSequenceId = beginSequenceId + estimatedResourceNumber;

                ImportProcessingTaskInputData importTaskPayload = new ImportProcessingTaskInputData()
                {
                    ResourceLocation = input.Url.ToString(),
                    UriString = _orchestratorInputData.RequestUri.ToString(),
                    BaseUriString = _orchestratorInputData.BaseUri.ToString(),
                    ResourceType = input.Type,
                    TaskId = taskId,
                    BeginSequenceId = beginSequenceId,
                    EndSequenceId = endSequenceId,
                };

                TaskInfo processingTask = new TaskInfo()
                {
                    QueueId = _orchestratorInputData.ProcessingTaskQueueId,
                    TaskId = taskId,
                    TaskTypeId = ImportProcessingTask.ImportProcessingTaskId,
                    Definition = JsonConvert.SerializeObject(importTaskPayload),
                    MaxRetryCount = _orchestratorInputData.ProcessingTaskMaxRetryCount,
                };

                result[input.Url] = processingTask;

                beginSequenceId = endSequenceId;

                totalSizeInBytes += blobSizeInBytes;
            }

            _importTaskResult.TotalSizeInBytes = totalSizeInBytes;

            return result;
        }

        private async Task CancelProcessingTasksAsync()
        {
            try
            {
                await _queueClient.CancelTaskAsync(_taskInfo.QueueType, _taskInfo.GroupId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "failed to cancel task {0}", _taskInfo.GroupId);
            }

            // Wait task cancel for WaitRunningTaskCancelTimeoutInSec
            // await WaitRunningTaskCompleteAsync();
        }
    }
}
