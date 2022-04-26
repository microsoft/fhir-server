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
        private ImportOrchestratorTaskContext _orchestratorTaskContext;
        private ITaskManager _taskManager;
        private ISequenceIdGenerator<long> _sequenceIdGenerator;
        private IImportOrchestratorTaskDataStoreOperation _importOrchestratorTaskDataStoreOperation;
        private IContextUpdater _contextUpdater;
        private ILogger<ImportOrchestratorTask> _logger;
        private IIntegrationDataStoreClient _integrationDataStoreClient;
        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private List<(Uri resourceUri, TaskInfo taskInfo)> _runningTasks = new List<(Uri resourceUri, TaskInfo taskInfo)>();

        public ImportOrchestratorTask(
            IMediator mediator,
            ImportOrchestratorTaskInputData orchestratorInputData,
            ImportOrchestratorTaskContext orchestratorTaskContext,
            ITaskManager taskManager,
            ISequenceIdGenerator<long> sequenceIdGenerator,
            IContextUpdater contextUpdater,
            RequestContextAccessor<IFhirRequestContext> contextAccessor,
            IImportOrchestratorTaskDataStoreOperation importOrchestratorTaskDataStoreOperation,
            IIntegrationDataStoreClient integrationDataStoreClient,
            ILoggerFactory loggerFactory)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(orchestratorInputData, nameof(orchestratorInputData));
            EnsureArg.IsNotNull(orchestratorTaskContext, nameof(orchestratorTaskContext));
            EnsureArg.IsNotNull(taskManager, nameof(taskManager));
            EnsureArg.IsNotNull(sequenceIdGenerator, nameof(sequenceIdGenerator));
            EnsureArg.IsNotNull(contextUpdater, nameof(contextUpdater));
            EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));
            EnsureArg.IsNotNull(importOrchestratorTaskDataStoreOperation, nameof(importOrchestratorTaskDataStoreOperation));
            EnsureArg.IsNotNull(integrationDataStoreClient, nameof(integrationDataStoreClient));
            EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory));

            _mediator = mediator;
            _orchestratorInputData = orchestratorInputData;
            _orchestratorTaskContext = orchestratorTaskContext;
            _taskManager = taskManager;
            _sequenceIdGenerator = sequenceIdGenerator;
            _contextUpdater = contextUpdater;
            _contextAccessor = contextAccessor;
            _importOrchestratorTaskDataStoreOperation = importOrchestratorTaskDataStoreOperation;
            _integrationDataStoreClient = integrationDataStoreClient;
            _logger = loggerFactory.CreateLogger<ImportOrchestratorTask>();
        }

        public string RunId { get; set; }

        public int PollingFrequencyInSeconds { get; set; } = DefaultPollingFrequencyInSeconds;

        public async Task<TaskResultData> ExecuteAsync()
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

            CancellationToken cancellationToken = _cancellationTokenSource.Token;

            TaskResultData taskResultData = null;
            ImportTaskErrorResult errorResult = null;
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException();
                }

                if (_orchestratorTaskContext.Progress == ImportOrchestratorTaskProgress.Initialized)
                {
                    await ValidateResourcesAsync(cancellationToken);

                    _orchestratorTaskContext.Progress = ImportOrchestratorTaskProgress.InputResourcesValidated;
                    await UpdateProgressAsync(_orchestratorTaskContext, cancellationToken);

                    _logger.LogInformation("Input Resources Validated");
                }

                if (_orchestratorTaskContext.Progress == ImportOrchestratorTaskProgress.InputResourcesValidated)
                {
                    await _importOrchestratorTaskDataStoreOperation.PreprocessAsync(cancellationToken);

                    _orchestratorTaskContext.Progress = ImportOrchestratorTaskProgress.PreprocessCompleted;
                    _orchestratorTaskContext.CurrentSequenceId = _sequenceIdGenerator.GetCurrentSequenceId();
                    await UpdateProgressAsync(_orchestratorTaskContext, cancellationToken);

                    _logger.LogInformation("Preprocess Completed");
                }

                if (_orchestratorInputData.StoreProgressInSubTask)
                {
                    if (_orchestratorTaskContext.Progress == ImportOrchestratorTaskProgress.PreprocessCompleted)
                    {
                        await ExecuteImprotProcessingTaskAsync(cancellationToken);
                        _orchestratorTaskContext.Progress = ImportOrchestratorTaskProgress.SubTasksCompleted;
                        await UpdateProgressAsync(_orchestratorTaskContext, cancellationToken);

                        _orchestratorTaskContext.ImportResult = new ImportTaskResult();
                        _logger.LogInformation("SubTasks Completed");
                    }
                }
                else
                {
                    // Should deprecate after schema upgrade to latest.
                    if (_orchestratorTaskContext.Progress == ImportOrchestratorTaskProgress.PreprocessCompleted)
                    {
                        _orchestratorTaskContext.DataProcessingTasks = await GenerateSubTaskRecordsAsync(cancellationToken);
                        _orchestratorTaskContext.Progress = ImportOrchestratorTaskProgress.SubTaskRecordsGenerated;
                        await UpdateProgressAsync(_orchestratorTaskContext, cancellationToken);

                        _logger.LogInformation("SubTask Records Generated");
                    }

                    if (_orchestratorTaskContext.Progress == ImportOrchestratorTaskProgress.SubTaskRecordsGenerated)
                    {
                        _orchestratorTaskContext.ImportResult = await ExecuteDataProcessingTasksAsync(cancellationToken);

                        _orchestratorTaskContext.Progress = ImportOrchestratorTaskProgress.SubTasksCompleted;
                        await UpdateProgressAsync(_orchestratorTaskContext, cancellationToken);

                        _logger.LogInformation("SubTasks Completed");
                    }
                }

                _orchestratorTaskContext.ImportResult.Request = _orchestratorInputData.RequestUri.ToString();
                _orchestratorTaskContext.ImportResult.TransactionTime = _orchestratorInputData.TaskCreateTime;
            }
            catch (TaskCanceledException taskCanceledEx)
            {
                _logger.LogInformation(taskCanceledEx, "Import task canceled. {0}", taskCanceledEx.Message);

                await CancelProcessingTasksAsync();
                taskResultData = new TaskResultData(TaskResult.Canceled, taskCanceledEx.Message);
            }
            catch (OperationCanceledException canceledEx)
            {
                _logger.LogInformation(canceledEx, "Import task canceled. {0}", canceledEx.Message);

                await CancelProcessingTasksAsync();
                taskResultData = new TaskResultData(TaskResult.Canceled, canceledEx.Message);
            }
            catch (IntegrationDataStoreException integrationDataStoreEx)
            {
                _logger.LogInformation(integrationDataStoreEx, "Failed to access input files.");

                errorResult = new ImportTaskErrorResult()
                {
                    HttpStatusCode = integrationDataStoreEx.StatusCode,
                    ErrorMessage = integrationDataStoreEx.Message,
                };

                taskResultData = new TaskResultData(TaskResult.Fail, JsonConvert.SerializeObject(errorResult));
            }
            catch (ImportFileEtagNotMatchException eTagEx)
            {
                _logger.LogInformation(eTagEx, "Import file etag not match.");

                errorResult = new ImportTaskErrorResult()
                {
                    HttpStatusCode = HttpStatusCode.BadRequest,
                    ErrorMessage = eTagEx.Message,
                };

                taskResultData = new TaskResultData(TaskResult.Fail, JsonConvert.SerializeObject(errorResult));
            }
            catch (ImportProcessingException processingEx)
            {
                _logger.LogInformation(processingEx, "Failed to process input resources.");

                errorResult = new ImportTaskErrorResult()
                {
                    HttpStatusCode = HttpStatusCode.BadRequest,
                    ErrorMessage = processingEx.Message,
                };

                taskResultData = new TaskResultData(TaskResult.Fail, JsonConvert.SerializeObject(errorResult));
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "Failed to import data.");

                errorResult = new ImportTaskErrorResult()
                {
                    HttpStatusCode = HttpStatusCode.InternalServerError,
                    ErrorMessage = ex.Message,
                };

                await SendImportMetricsNotification(TaskResult.Fail);

                throw new RetriableTaskException(JsonConvert.SerializeObject(errorResult));
            }

            if (_orchestratorTaskContext.Progress > ImportOrchestratorTaskProgress.InputResourcesValidated)
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

                    ImportTaskErrorResult postProcessEerrorResult = new ImportTaskErrorResult()
                    {
                        HttpStatusCode = HttpStatusCode.InternalServerError,
                        ErrorMessage = ex.Message,

                        // other error if any.
                        InnerError = errorResult,
                    };

                    await SendImportMetricsNotification(TaskResult.Fail);

                    throw new RetriableTaskException(JsonConvert.SerializeObject(postProcessEerrorResult));
                }
            }

            if (taskResultData == null) // No exception
            {
                taskResultData = new TaskResultData(TaskResult.Success, JsonConvert.SerializeObject(_orchestratorTaskContext.ImportResult));
            }

            await SendImportMetricsNotification(taskResultData.Result);

            return taskResultData;
        }

        public void Cancel()
        {
            if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
            {
                _cancellationTokenSource?.Cancel();
            }
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
                _orchestratorTaskContext.TotalSizeInBytes,
                _orchestratorTaskContext.SucceedImportCount,
                _orchestratorTaskContext.FailedImportCount);

            await _mediator.Publish(importTaskMetricsNotification, CancellationToken.None);
        }

        private async Task UpdateProgressAsync(ImportOrchestratorTaskContext context, CancellationToken cancellationToken)
        {
            await _contextUpdater.UpdateContextAsync(JsonConvert.SerializeObject(context), cancellationToken);
        }

        private async Task ExecuteImprotProcessingTaskAsync(CancellationToken cancellationToken)
        {
            _orchestratorTaskContext.TotalSizeInBytes = _orchestratorTaskContext.TotalSizeInBytes ?? 0;

            foreach (var input in _orchestratorInputData.Input.Skip(_orchestratorTaskContext.CreatedTaskCount))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException();
                }

                while (_orchestratorTaskContext.RunningTaskIds.Count >= _orchestratorInputData.MaxConcurrentProcessingTaskCount)
                {
                    HashSet<string> completedTaskIds = new HashSet<string>();
                    foreach (string taskId in _orchestratorTaskContext.RunningTaskIds)
                    {
                        TaskInfo latestTaskInfo = await _taskManager.GetTaskAsync(taskId, cancellationToken);

                        if (latestTaskInfo.Status == TaskStatus.Completed)
                        {
                            CheckTaskResult(latestTaskInfo);
                            completedTaskIds.Add(taskId);
                        }
                    }

                    if (completedTaskIds.Count > 0)
                    {
                        _orchestratorTaskContext.RunningTaskIds.RemoveAll(id => completedTaskIds.Contains(id));
                        await UpdateProgressAsync(_orchestratorTaskContext, cancellationToken);
                    }

                    await Task.Delay(TimeSpan.FromSeconds(PollingFrequencyInSeconds), cancellationToken);
                }

                (string processingTaskId, long endSequenceId, long blobSizeInBytes) = await CreateNewProcessingTaskAsync(input, cancellationToken);

                _orchestratorTaskContext.RunningTaskIds.Add(processingTaskId);
                _orchestratorTaskContext.CurrentSequenceId = endSequenceId;
                _orchestratorTaskContext.TotalSizeInBytes += blobSizeInBytes;
                _orchestratorTaskContext.CreatedTaskCount += 1;
                await UpdateProgressAsync(_orchestratorTaskContext, cancellationToken);
            }

            while (_orchestratorTaskContext.RunningTaskIds.Count > 0)
            {
                HashSet<string> completedTaskIds = new HashSet<string>();
                foreach (string taskId in _orchestratorTaskContext.RunningTaskIds)
                {
                    TaskInfo latestTaskInfo = await _taskManager.GetTaskAsync(taskId, cancellationToken);

                    if (latestTaskInfo.Status == TaskStatus.Completed)
                    {
                        CheckTaskResult(latestTaskInfo);
                        completedTaskIds.Add(taskId);
                    }
                }

                if (completedTaskIds.Count > 0)
                {
                    _orchestratorTaskContext.RunningTaskIds.RemoveAll(id => completedTaskIds.Contains(id));
                    await UpdateProgressAsync(_orchestratorTaskContext, cancellationToken);
                }

                await Task.Delay(TimeSpan.FromSeconds(PollingFrequencyInSeconds), cancellationToken);
            }
        }

        private async Task<(string taskId, long endSequenceId, long blobSizeInBytes)> CreateNewProcessingTaskAsync(Models.InputResource input, CancellationToken cancellationToken)
        {
            string processingTaskId = $"{_orchestratorInputData.TaskId}_{_orchestratorTaskContext.CreatedTaskCount}";

            Dictionary<string, object> properties = await _integrationDataStoreClient.GetPropertiesAsync(input.Url, cancellationToken);
            long blobSizeInBytes = (long)properties[IntegrationDataStoreClientConstants.BlobPropertyLength];
            long estimatedResourceNumber = CalculateResourceNumberByResourceSize(blobSizeInBytes, DefaultResourceSizePerByte);
            long beginSequenceId = _orchestratorTaskContext.CurrentSequenceId;
            long endSequenceId = beginSequenceId + estimatedResourceNumber;

            ImportProcessingTaskInputData importTaskPayload = new ImportProcessingTaskInputData()
            {
                ResourceLocation = input.Url.ToString(),
                UriString = _orchestratorInputData.RequestUri.ToString(),
                BaseUriString = _orchestratorInputData.BaseUri.ToString(),
                ResourceType = input.Type,
                TaskId = processingTaskId,
                BeginSequenceId = beginSequenceId,
                EndSequenceId = endSequenceId,
            };

            TaskInfo processingTask = new TaskInfo()
            {
                QueueId = _orchestratorInputData.ProcessingTaskQueueId,
                TaskId = processingTaskId,
                TaskTypeId = ImportProcessingTask.ImportProcessingTaskId,
                InputData = JsonConvert.SerializeObject(importTaskPayload),
                MaxRetryCount = _orchestratorInputData.ProcessingTaskMaxRetryCount,
                ParentTaskId = _orchestratorInputData.TaskId,
            };

            TaskInfo taskInfoFromServer = await _taskManager.GetTaskAsync(processingTaskId, cancellationToken);
            if (taskInfoFromServer == null)
            {
                taskInfoFromServer = await _taskManager.CreateTaskAsync(processingTask, false, cancellationToken);
            }

            return (taskInfoFromServer.TaskId, endSequenceId, blobSizeInBytes);
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
                _orchestratorTaskContext.SucceedImportCount += procesingTaskResult.SucceedCount;
                _orchestratorTaskContext.FailedImportCount += procesingTaskResult.FailedCount;
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
                    InputData = JsonConvert.SerializeObject(importTaskPayload),
                    MaxRetryCount = _orchestratorInputData.ProcessingTaskMaxRetryCount,
                };

                result[input.Url] = processingTask;

                beginSequenceId = endSequenceId;

                totalSizeInBytes += blobSizeInBytes;
            }

            _orchestratorTaskContext.TotalSizeInBytes = totalSizeInBytes;

            return result;
        }

        // Should deprecate after schema upgrade
        private async Task<ImportTaskResult> ExecuteDataProcessingTasksAsync(CancellationToken cancellationToken)
        {
            List<ImportOperationOutcome> completedOperationOutcome = new List<ImportOperationOutcome>();
            List<ImportFailedOperationOutcome> failedOperationOutcome = new List<ImportFailedOperationOutcome>();

            foreach ((Uri resourceUri, TaskInfo taskInfo) in _orchestratorTaskContext.DataProcessingTasks.ToArray())
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException();
                }

                while (_runningTasks.Count >= _orchestratorInputData.MaxConcurrentProcessingTaskCount)
                {
                    List<Uri> completedTaskResourceUris = await MonitorRunningTasksAsync(_runningTasks, cancellationToken);

                    if (completedTaskResourceUris.Count > 0)
                    {
                        AddToResult(completedOperationOutcome, failedOperationOutcome, completedTaskResourceUris);

                        _runningTasks.RemoveAll(t => completedTaskResourceUris.Contains(t.resourceUri));
                        await UpdateProgressAsync(_orchestratorTaskContext, cancellationToken);
                    }
                    else
                    {
                        await Task.Delay(TimeSpan.FromSeconds(PollingFrequencyInSeconds), cancellationToken);
                    }
                }

                TaskInfo taskInfoFromServer = await _taskManager.GetTaskAsync(taskInfo.TaskId, cancellationToken);
                if (taskInfoFromServer == null)
                {
                    taskInfoFromServer = await _taskManager.CreateTaskAsync(taskInfo, false, cancellationToken);
                }

                _orchestratorTaskContext.DataProcessingTasks[resourceUri] = taskInfoFromServer;
                if (taskInfoFromServer.Status != TaskStatus.Completed)
                {
                    _runningTasks.Add((resourceUri, taskInfoFromServer));
                    await Task.Delay(TimeSpan.FromSeconds(PollingFrequencyInSeconds), cancellationToken);
                }
                else
                {
                    AddToResult(completedOperationOutcome, failedOperationOutcome, new List<Uri>() { resourceUri });
                }
            }

            while (_runningTasks.Count > 0)
            {
                List<Uri> completedTaskResourceUris = await MonitorRunningTasksAsync(_runningTasks, cancellationToken);

                if (completedTaskResourceUris.Count > 0)
                {
                    AddToResult(completedOperationOutcome, failedOperationOutcome, completedTaskResourceUris);

                    _runningTasks.RemoveAll(t => completedTaskResourceUris.Contains(t.resourceUri));
                    await UpdateProgressAsync(_orchestratorTaskContext, cancellationToken);
                }
                else
                {
                    await Task.Delay(TimeSpan.FromSeconds(PollingFrequencyInSeconds), cancellationToken);
                }
            }

            return new ImportTaskResult()
            {
                Output = completedOperationOutcome,
                Error = failedOperationOutcome,
            };
        }

        private void AddToResult(List<ImportOperationOutcome> completedOperationOutcome, List<ImportFailedOperationOutcome> failedOperationOutcome, List<Uri> completedTaskResourceUris)
        {
            foreach (Uri completedResourceUri in completedTaskResourceUris)
            {
                TaskInfo completeTaskInfo = _orchestratorTaskContext.DataProcessingTasks[completedResourceUri];
                TaskResultData taskResultData = JsonConvert.DeserializeObject<TaskResultData>(completeTaskInfo.Result);

                if (taskResultData.Result == TaskResult.Success)
                {
                    ImportProcessingTaskResult procesingTaskResult = JsonConvert.DeserializeObject<ImportProcessingTaskResult>(taskResultData.ResultData);
                    completedOperationOutcome.Add(new ImportOperationOutcome() { Type = procesingTaskResult.ResourceType, Count = procesingTaskResult.SucceedCount, InputUrl = completedResourceUri });
                    if (procesingTaskResult.FailedCount > 0)
                    {
                        failedOperationOutcome.Add(new ImportFailedOperationOutcome() { Type = procesingTaskResult.ResourceType, Count = procesingTaskResult.FailedCount, InputUrl = completedResourceUri, Url = procesingTaskResult.ErrorLogLocation });
                    }
                }
                else if (taskResultData.Result == TaskResult.Fail)
                {
                    throw new ImportProcessingException(string.Format("Failed to process file: {0}. {1}", completedResourceUri, taskResultData));
                }
                else if (taskResultData.Result == TaskResult.Canceled)
                {
                    throw new OperationCanceledException(taskResultData.ResultData);
                }
            }
        }

        private async Task<List<Uri>> MonitorRunningTasksAsync(List<(Uri resourceUri, TaskInfo taskInfo)> runningTasks, CancellationToken cancellationToken)
        {
            List<Uri> completedTaskResourceUris = new List<Uri>();

            foreach ((Uri runningResourceUri, TaskInfo runningTaskInfo) in runningTasks)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException();
                }

                TaskInfo latestTaskInfo = await _taskManager.GetTaskAsync(runningTaskInfo.TaskId, cancellationToken);

                _orchestratorTaskContext.DataProcessingTasks[runningResourceUri] = latestTaskInfo;
                if (latestTaskInfo.Status == TaskStatus.Completed)
                {
                    completedTaskResourceUris.Add(runningResourceUri);
                }
            }

            return completedTaskResourceUris;
        }

        private async Task CancelProcessingTasksAsync()
        {
            if ((_orchestratorTaskContext?.RunningTaskIds?.Count ?? 0) == 0)
            {
                // No data processing task running.
                return;
            }

            foreach (string taskId in _orchestratorTaskContext.RunningTaskIds)
            {
                try
                {
                    await _taskManager.CancelTaskAsync(taskId, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "failed to cancel task {0}", taskId);
                }
            }

            // Wait task cancel for WaitRunningTaskCancelTimeoutInSec
            await WaitRunningTaskCompleteAsync(_orchestratorTaskContext.RunningTaskIds);
        }

        private async Task WaitRunningTaskCompleteAsync(List<string> runningTaskIds)
        {
            while (true)
            {
                if (runningTaskIds.Count == 0)
                {
                    break;
                }

                string[] currentRunningTaskIds = runningTaskIds.ToArray();

                foreach (string runningTaskId in currentRunningTaskIds)
                {
                    try
                    {
                        TaskInfo taskInfo = await _taskManager.GetTaskAsync(runningTaskId, CancellationToken.None);
                        if (taskInfo == null || taskInfo.Status == TaskStatus.Completed)
                        {
                            runningTaskIds.Remove(runningTaskId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to get task info for canceled task {0}", runningTaskId);
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }
    }
}
