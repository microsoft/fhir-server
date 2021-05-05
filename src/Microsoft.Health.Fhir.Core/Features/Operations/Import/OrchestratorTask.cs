// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.TaskManagement;
using Newtonsoft.Json;
using TaskStatus = Microsoft.Health.TaskManagement.TaskStatus;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class OrchestratorTask : ITask
    {
        public const short ImportOrchestratorTaskId = 2;

        private const int PollingFrequencyInSeconds = 3;
        private const long MinResourceSizeInBytes = 64;

        private ImportOrchestratorTaskInputData _orchestratorInputData;
        private RequestContextAccessor<IFhirRequestContext> _contextAccessor;
        private OrchestratorTaskContext _orchestratorTaskContext; // TODO : changed later
        private ITaskManager _taskManager;
        private ISequenceIdGenerator<long> _sequenceIdGenerator;
        private IFhirDataBulkImportOperation _fhirDataBulkImportOperation;
        private IContextUpdater _contextUpdater;
        private ILogger<OrchestratorTask> _logger;
        private IIntegrationDataStoreClient _integrationDataStoreClient;
        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        public OrchestratorTask(
            ImportOrchestratorTaskInputData orchestratorInputData,
            OrchestratorTaskContext orchestratorTaskContext,
            ITaskManager taskManager,
            ISequenceIdGenerator<long> sequenceIdGenerator,
            IContextUpdater contextUpdater,
            RequestContextAccessor<IFhirRequestContext> contextAccessor,
            IFhirDataBulkImportOperation fhirDataBulkImportOperation,
            IIntegrationDataStoreClient integrationDataStoreClient,
            ILoggerFactory loggerFactory)
        {
            EnsureArg.IsNotNull(orchestratorInputData, nameof(orchestratorInputData));
            EnsureArg.IsNotNull(orchestratorTaskContext, nameof(orchestratorTaskContext));
            EnsureArg.IsNotNull(taskManager, nameof(taskManager));
            EnsureArg.IsNotNull(sequenceIdGenerator, nameof(sequenceIdGenerator));
            EnsureArg.IsNotNull(contextUpdater, nameof(contextUpdater));
            EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));
            EnsureArg.IsNotNull(fhirDataBulkImportOperation, nameof(fhirDataBulkImportOperation));
            EnsureArg.IsNotNull(integrationDataStoreClient, nameof(integrationDataStoreClient));
            EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory));

            _orchestratorInputData = orchestratorInputData;
            _orchestratorTaskContext = orchestratorTaskContext;
            _taskManager = taskManager;
            _sequenceIdGenerator = sequenceIdGenerator;
            _contextUpdater = contextUpdater;
            _contextAccessor = contextAccessor;
            _fhirDataBulkImportOperation = fhirDataBulkImportOperation;
            _integrationDataStoreClient = integrationDataStoreClient;
            _logger = loggerFactory.CreateLogger<OrchestratorTask>();
        }

        public string RunId { get; set; }

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

            try
            {
                if (_orchestratorTaskContext.Progress == OrchestratorTaskProgress.Initalized)
                {
                    await ValidateResourcesAsync(cancellationToken);

                    _orchestratorTaskContext.Progress = OrchestratorTaskProgress.InputResourcesValidated;
                    await UpdateProgressAsync(_orchestratorTaskContext, cancellationToken);
                }

                if (_orchestratorTaskContext.Progress == OrchestratorTaskProgress.InputResourcesValidated)
                {
                    await _fhirDataBulkImportOperation.DisableIndexesAsync(cancellationToken);

                    _orchestratorTaskContext.Progress = OrchestratorTaskProgress.PreprocessCompleted;
                    await UpdateProgressAsync(_orchestratorTaskContext, cancellationToken);
                }

                if (_orchestratorTaskContext.Progress == OrchestratorTaskProgress.PreprocessCompleted)
                {
                    _orchestratorTaskContext.DataProcessingTasks = await GenerateSubTaskRecordsAsync(cancellationToken);
                    _orchestratorTaskContext.Progress = OrchestratorTaskProgress.SubTaskRecordsGenerated;
                    await UpdateProgressAsync(_orchestratorTaskContext, cancellationToken);
                }

                if (_orchestratorTaskContext.Progress == OrchestratorTaskProgress.SubTaskRecordsGenerated)
                {
                    await ExecuteDataProcessingTasksAsync(cancellationToken);

                    _orchestratorTaskContext.Progress = OrchestratorTaskProgress.SubTasksCompleted;
                    await UpdateProgressAsync(_orchestratorTaskContext, cancellationToken);
                }

                if (_orchestratorTaskContext.Progress == OrchestratorTaskProgress.SubTasksCompleted)
                {
                    await _fhirDataBulkImportOperation.DeleteDuplicatedResourcesAsync(cancellationToken);
                    await _fhirDataBulkImportOperation.RebuildIndexesAsync(cancellationToken);

                    _orchestratorTaskContext.Progress = OrchestratorTaskProgress.PostprocessCompleted;
                    await UpdateProgressAsync(_orchestratorTaskContext, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                // DO THINK: shuld we recover the db here?
                // log here and throw and return error result
                throw new RetriableTaskException(ex.Message);
            }

            return new TaskResultData(TaskResult.Success, string.Empty);
        }

        private async Task ValidateResourcesAsync(CancellationToken cancellationToken)
        {
            foreach (var input in _orchestratorInputData.Input)
            {
                if (!string.IsNullOrEmpty(input.Etag))
                {
                    Dictionary<string, object> properties = await _integrationDataStoreClient.GetPropertiesAsync(input.Url, cancellationToken);
                    if (!input.Etag.Equals(properties[IntegrationDataStoreClientConstants.BlobPropertyETag]))
                    {
                        throw new ImportFileEtagNotMatchException(string.Format("Input file Etag not match. {0}", input.Url));
                    }
                }
            }
        }

        private async Task UpdateProgressAsync(OrchestratorTaskContext context, CancellationToken cancellationToken)
        {
            await _contextUpdater.UpdateContextAsync(JsonConvert.SerializeObject(context), cancellationToken);
        }

        private async Task<Dictionary<Uri, TaskInfo>> GenerateSubTaskRecordsAsync(CancellationToken cancellationToken)
        {
            Dictionary<Uri, TaskInfo> result = new Dictionary<Uri, TaskInfo>();

            long beginSequenceId = _sequenceIdGenerator.GetCurrentSequenceId();

            foreach (var input in _orchestratorInputData.Input)
            {
                string taskId = Guid.NewGuid().ToString("N");

                Dictionary<string, object> properties = await _integrationDataStoreClient.GetPropertiesAsync(input.Url, cancellationToken);
                long blobSizeInBytes = (long)properties[IntegrationDataStoreClientConstants.BlobPropertyLength];

                long estimatedResourceNumber = (blobSizeInBytes / MinResourceSizeInBytes) + 1;
                long endSequenceId = beginSequenceId + estimatedResourceNumber;

                ImportTaskInputData importTaskPayload = new ImportTaskInputData()
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
                    TaskTypeId = ImportTask.ImportProcessingTaskId,
                    InputData = JsonConvert.SerializeObject(importTaskPayload),
                };

                result[input.Url] = processingTask;

                beginSequenceId = endSequenceId;
            }

            return result;
        }

        private async Task ExecuteDataProcessingTasksAsync(CancellationToken cancellationToken)
        {
            List<(Uri resourceUri, TaskInfo taskInfo)> runningTasks = new List<(Uri resourceUri, TaskInfo taskInfo)>();

            foreach ((Uri resourceUri, TaskInfo taskInfo) in _orchestratorTaskContext.DataProcessingTasks.ToArray())
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException();
                }

                while (runningTasks.Count >= _orchestratorInputData.MaxConcurrentProcessingTaskCount)
                {
                    await MonitorRunningTasks(runningTasks, cancellationToken);
                }

                TaskInfo taskInfoFromServer = await _taskManager.GetTaskAsync(taskInfo.TaskId, cancellationToken);
                if (taskInfoFromServer == null)
                {
                    taskInfoFromServer = await _taskManager.CreateTaskAsync(taskInfo, cancellationToken);
                }

                _orchestratorTaskContext.DataProcessingTasks[resourceUri] = taskInfoFromServer;
                if (taskInfoFromServer.Status != TaskStatus.Completed)
                {
                    runningTasks.Add((resourceUri, taskInfoFromServer));
                    await Task.Delay(TimeSpan.FromSeconds(PollingFrequencyInSeconds), cancellationToken);
                }
            }

            while (runningTasks.Count > 0)
            {
                await MonitorRunningTasks(runningTasks, cancellationToken);
            }
        }

        private async Task MonitorRunningTasks(List<(Uri resourceUri, TaskInfo taskInfo)> runningTasks, CancellationToken cancellationToken)
        {
            List<Uri> completedTaskResourceUris = new List<Uri>();

            foreach ((Uri runningResourceUri, TaskInfo runningTaskInfo) in runningTasks)
            {
                TaskInfo latestTaskInfo = await _taskManager.GetTaskAsync(runningTaskInfo.TaskId, cancellationToken);

                _orchestratorTaskContext.DataProcessingTasks[runningResourceUri] = latestTaskInfo;
                if (latestTaskInfo.Status == TaskStatus.Completed)
                {
                    completedTaskResourceUris.Add(runningResourceUri);
                }

                // TODO Aggregate task result
            }

            runningTasks.RemoveAll(t => completedTaskResourceUris.Contains(t.resourceUri));
            await Task.Delay(TimeSpan.FromSeconds(PollingFrequencyInSeconds), cancellationToken);
        }

        public void Cancel()
        {
            _cancellationTokenSource?.Cancel();
        }

        public bool IsCancelling()
        {
            return _cancellationTokenSource?.IsCancellationRequested ?? false;
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
