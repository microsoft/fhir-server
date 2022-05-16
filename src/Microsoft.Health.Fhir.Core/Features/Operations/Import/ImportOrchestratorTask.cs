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
        private ImportOrchestratorTaskResult _orchestratorTaskResult;
        private IImportOrchestratorTaskDataStoreOperation _importOrchestratorTaskDataStoreOperation;
        private IQueueClient _queueClient;
        private TaskInfo _taskInfo;
        private ImportTaskConfiguration _importTaskConfiguration;
        private ILogger<ImportOrchestratorTask> _logger;
        private IIntegrationDataStoreClient _integrationDataStoreClient;

        public ImportOrchestratorTask(
            IMediator mediator,
            ImportOrchestratorTaskInputData orchestratorInputData,
            ImportOrchestratorTaskResult orchrestratorTaskResult,
            RequestContextAccessor<IFhirRequestContext> contextAccessor,
            IImportOrchestratorTaskDataStoreOperation importOrchestratorTaskDataStoreOperation,
            IIntegrationDataStoreClient integrationDataStoreClient,
            IQueueClient queueClient,
            TaskInfo taskInfo,
            ImportTaskConfiguration importTaskConfiguration,
            ILoggerFactory loggerFactory)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(orchestratorInputData, nameof(orchestratorInputData));
            EnsureArg.IsNotNull(orchrestratorTaskResult, nameof(orchrestratorTaskResult));
            EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));
            EnsureArg.IsNotNull(importOrchestratorTaskDataStoreOperation, nameof(importOrchestratorTaskDataStoreOperation));
            EnsureArg.IsNotNull(integrationDataStoreClient, nameof(integrationDataStoreClient));
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            EnsureArg.IsNotNull(taskInfo, nameof(taskInfo));
            EnsureArg.IsNotNull(importTaskConfiguration, nameof(importTaskConfiguration));
            EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory));

            _mediator = mediator;
            _orchestratorInputData = orchestratorInputData;
            _orchestratorTaskResult = orchrestratorTaskResult;
            _contextAccessor = contextAccessor;
            _importOrchestratorTaskDataStoreOperation = importOrchestratorTaskDataStoreOperation;
            _integrationDataStoreClient = integrationDataStoreClient;
            _queueClient = queueClient;
            _taskInfo = taskInfo;
            _importTaskConfiguration = importTaskConfiguration;
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
                    correlationId: _taskInfo.Id.ToString(),
                    requestHeaders: new Dictionary<string, StringValues>(),
                    responseHeaders: new Dictionary<string, StringValues>())
            {
                IsBackgroundTask = true,
            };

            _contextAccessor.RequestContext = fhirRequestContext;

            _orchestratorTaskResult.Request = _orchestratorInputData.RequestUri.ToString();
            _orchestratorTaskResult.TransactionTime = _orchestratorInputData.TaskCreateTime;

            ImportOrchestratorTaskErrorResult errorResult = null;

            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException();
                }

                if (_orchestratorTaskResult.Progress == ImportOrchestratorTaskProgress.Initialized)
                {
                    await ValidateResourcesAsync(cancellationToken);

                    _orchestratorTaskResult.Progress = ImportOrchestratorTaskProgress.InputResourcesValidated;
                    progress.Report(JsonConvert.SerializeObject(_orchestratorTaskResult));

                    _logger.LogInformation("Input Resources Validated");
                }

                if (_orchestratorTaskResult.Progress == ImportOrchestratorTaskProgress.InputResourcesValidated)
                {
                    await _importOrchestratorTaskDataStoreOperation.PreprocessAsync(cancellationToken);

                    _orchestratorTaskResult.Progress = ImportOrchestratorTaskProgress.PreprocessCompleted;
                    _orchestratorTaskResult.CurrentSequenceId = _orchestratorInputData.StartSequenceId;
                    progress.Report(JsonConvert.SerializeObject(_orchestratorTaskResult));

                    _logger.LogInformation("Preprocess Completed");
                }

                if (_orchestratorTaskResult.Progress == ImportOrchestratorTaskProgress.PreprocessCompleted)
                {
                    await ExecuteImportProcessingTaskAsync(progress, cancellationToken);
                    _orchestratorTaskResult.Progress = ImportOrchestratorTaskProgress.SubTasksCompleted;
                    progress.Report(JsonConvert.SerializeObject(_orchestratorTaskResult));

                    _logger.LogInformation("SubTasks Completed");
                }
            }
            catch (TaskCanceledException taskCanceledEx)
            {
                _logger.LogInformation(taskCanceledEx, "Import task canceled. {0}", taskCanceledEx.Message);

                errorResult = new ImportOrchestratorTaskErrorResult()
                {
                    HttpStatusCode = HttpStatusCode.BadRequest,
                    ErrorMessage = taskCanceledEx.Message,
                };

                await CancelProcessingTasksAsync();
                await SendImportMetricsNotification(TaskResult.Canceled);
            }
            catch (OperationCanceledException canceledEx)
            {
                _logger.LogInformation(canceledEx, "Import task canceled. {0}", canceledEx.Message);

                errorResult = new ImportOrchestratorTaskErrorResult()
                {
                    HttpStatusCode = HttpStatusCode.BadRequest,
                    ErrorMessage = canceledEx.Message,
                };

                await CancelProcessingTasksAsync();
                await SendImportMetricsNotification(TaskResult.Canceled);
            }
            catch (IntegrationDataStoreException integrationDataStoreEx)
            {
                _logger.LogInformation(integrationDataStoreEx, "Failed to access input files.");

                errorResult = new ImportOrchestratorTaskErrorResult()
                {
                    HttpStatusCode = integrationDataStoreEx.StatusCode,
                    ErrorMessage = integrationDataStoreEx.Message,
                };

                await SendImportMetricsNotification(TaskResult.Fail);
            }
            catch (ImportFileEtagNotMatchException eTagEx)
            {
                _logger.LogInformation(eTagEx, "Import file etag not match.");

                errorResult = new ImportOrchestratorTaskErrorResult()
                {
                    HttpStatusCode = HttpStatusCode.BadRequest,
                    ErrorMessage = eTagEx.Message,
                };

                await SendImportMetricsNotification(TaskResult.Fail);
            }
            catch (ImportProcessingException processingEx)
            {
                _logger.LogInformation(processingEx, "Failed to process input resources.");

                errorResult = new ImportOrchestratorTaskErrorResult()
                {
                    HttpStatusCode = HttpStatusCode.BadRequest,
                    ErrorMessage = processingEx.Message,
                };

                await CancelProcessingTasksAsync();
                await SendImportMetricsNotification(TaskResult.Fail);
            }
            catch (RetriableTaskException ex)
            {
                _logger.LogInformation(ex, "Failed with RetriableTaskException.");

                throw;
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "Failed to import data.");

                errorResult = new ImportOrchestratorTaskErrorResult()
                {
                    HttpStatusCode = HttpStatusCode.InternalServerError,
                    ErrorMessage = ex.Message,
                };

                await CancelProcessingTasksAsync();
                await SendImportMetricsNotification(TaskResult.Fail);
            }

            // Post-process operation cannot be cancelled.
            try
            {
                await _importOrchestratorTaskDataStoreOperation.PostprocessAsync(CancellationToken.None);

                _logger.LogInformation("Postprocess Completed");
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "Failed at postprocess step.");

                ImportOrchestratorTaskErrorResult postProcessErrorResult = new ImportOrchestratorTaskErrorResult()
                {
                    HttpStatusCode = HttpStatusCode.InternalServerError,
                    ErrorMessage = ex.Message,
                };

                throw new RetriableTaskException(JsonConvert.SerializeObject(postProcessErrorResult));
            }

            if (errorResult != null)
            {
                throw new TaskExecutionException(errorResult.ErrorMessage, errorResult);
            }

            await SendImportMetricsNotification(TaskResult.Success);
            return JsonConvert.SerializeObject(_orchestratorTaskResult);
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
                _taskInfo.Id.ToString(),
                taskResult.ToString(),
                _orchestratorInputData.TaskCreateTime,
                Clock.UtcNow,
                _orchestratorTaskResult.TotalSizeInBytes,
                _orchestratorTaskResult.SucceedImportCount,
                _orchestratorTaskResult.FailedImportCount);

            await _mediator.Publish(importTaskMetricsNotification, CancellationToken.None);
        }

        private async Task ExecuteImportProcessingTaskAsync(IProgress<string> progress, CancellationToken cancellationToken)
        {
            _orchestratorTaskResult.TotalSizeInBytes = _orchestratorTaskResult.TotalSizeInBytes ?? 0;

            foreach (var input in _orchestratorInputData.Input.Skip(_orchestratorTaskResult.CreatedTaskCount))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException();
                }

                while (_orchestratorTaskResult.RunningTaskIds.Count >= _importTaskConfiguration.MaxRunningProcessingTaskCount)
                {
                    await WaitRunningTaskComplete(progress, cancellationToken);
                }

                (long processingTaskId, long endSequenceId, long blobSizeInBytes) = await CreateNewProcessingTaskAsync(input, cancellationToken);

                _orchestratorTaskResult.RunningTaskIds.Add(processingTaskId);
                _orchestratorTaskResult.CurrentSequenceId = endSequenceId;
                _orchestratorTaskResult.TotalSizeInBytes += blobSizeInBytes;
                _orchestratorTaskResult.CreatedTaskCount += 1;
                progress.Report(JsonConvert.SerializeObject(_orchestratorTaskResult));
            }

            while (_orchestratorTaskResult.RunningTaskIds.Count > 0)
            {
                await WaitRunningTaskComplete(progress, cancellationToken);
            }
        }

        private async Task WaitRunningTaskComplete(IProgress<string> progress, CancellationToken cancellationToken)
        {
            HashSet<long> completedTaskIds = new HashSet<long>();
            foreach (long taskId in _orchestratorTaskResult.RunningTaskIds)
            {
                TaskInfo latestTaskInfo = await _queueClient.GetTaskByIdAsync(_taskInfo.QueueType, taskId, false, cancellationToken);

                if (latestTaskInfo.Status != TaskStatus.Created && latestTaskInfo.Status != TaskStatus.Running)
                {
                    if (latestTaskInfo.Status == TaskStatus.Completed)
                    {
                        ImportProcessingTaskResult procesingTaskResult = JsonConvert.DeserializeObject<ImportProcessingTaskResult>(latestTaskInfo.Result);
                        _orchestratorTaskResult.SucceedImportCount += procesingTaskResult.SucceedCount;
                        _orchestratorTaskResult.FailedImportCount += procesingTaskResult.FailedCount;
                    }
                    else if (latestTaskInfo.Status == TaskStatus.Failed)
                    {
                        ImportProcessingTaskErrorResult procesingTaskResult = JsonConvert.DeserializeObject<ImportProcessingTaskErrorResult>(latestTaskInfo.Result);
                        throw new ImportProcessingException(procesingTaskResult.Message);
                    }
                    else if (latestTaskInfo.Status == TaskStatus.Cancelled)
                    {
                        throw new OperationCanceledException("Import operation cancelled by customer.");
                    }

                    completedTaskIds.Add(latestTaskInfo.Id);
                }
            }

            if (completedTaskIds.Count > 0)
            {
                _orchestratorTaskResult.RunningTaskIds.RemoveAll(id => completedTaskIds.Contains(id));
                progress.Report(JsonConvert.SerializeObject(_orchestratorTaskResult));
            }

            await Task.Delay(TimeSpan.FromSeconds(PollingFrequencyInSeconds), cancellationToken);
        }

        private async Task<(long taskId, long endSequenceId, long blobSizeInBytes)> CreateNewProcessingTaskAsync(Models.InputResource input, CancellationToken cancellationToken)
        {
            Dictionary<string, object> properties = await _integrationDataStoreClient.GetPropertiesAsync(input.Url, cancellationToken);
            long blobSizeInBytes = (long)properties[IntegrationDataStoreClientConstants.BlobPropertyLength];
            long estimatedResourceNumber = CalculateResourceNumberByResourceSize(blobSizeInBytes, DefaultResourceSizePerByte);
            long beginSequenceId = _orchestratorTaskResult.CurrentSequenceId;
            long endSequenceId = beginSequenceId + estimatedResourceNumber;

            ImportProcessingTaskInputData importTaskPayload = new ImportProcessingTaskInputData()
            {
                TypeId = ImportProcessingTask.ImportProcessingTaskId,
                ResourceLocation = input.Url.ToString(),
                UriString = _orchestratorInputData.RequestUri.ToString(),
                BaseUriString = _orchestratorInputData.BaseUri.ToString(),
                ResourceType = input.Type,
                BeginSequenceId = beginSequenceId,
                EndSequenceId = endSequenceId,
                TaskId = _taskInfo.GroupId.ToString(),
            };

            string[] definitions = new string[] { JsonConvert.SerializeObject(importTaskPayload) };

            TaskInfo taskInfoFromServer = (await _queueClient.EnqueueAsync(_taskInfo.QueueType, definitions, _taskInfo.GroupId, false, false, cancellationToken)).First();

            return (taskInfoFromServer.Id, endSequenceId, blobSizeInBytes);
        }

        private async Task CancelProcessingTasksAsync()
        {
            try
            {
                await _queueClient.CancelTaskByGroupIdAsync(_taskInfo.QueueType, _taskInfo.GroupId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "failed to cancel task {0}", _taskInfo.GroupId);
            }

            await WaitRunningTaskCompleteOrCancelledAsync();
        }

        private async Task WaitRunningTaskCompleteOrCancelledAsync()
        {
            while (true)
            {
                try
                {
                    IEnumerable<TaskInfo> taskInfos = await _queueClient.GetTaskByGroupIdAsync((byte)QueueType.Import, _taskInfo.GroupId, false, CancellationToken.None);

                    if (taskInfos.All(t => (t.Status != TaskStatus.Created && t.Status != TaskStatus.Running) || t.Id == _taskInfo.Id))
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "failed to get tasks by groupId {0}", _taskInfo.GroupId);
                    throw new RetriableTaskException(ex.Message, ex);
                }

                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }
    }
}
