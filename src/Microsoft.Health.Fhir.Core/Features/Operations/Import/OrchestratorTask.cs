// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DotLiquid.Util;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkImport.Models;
using Microsoft.Health.Fhir.Core.Messages.BulkImport;
using Microsoft.Health.Fhir.TaskManagement;
using Newtonsoft.Json;
using TaskStatus = Microsoft.Health.Fhir.TaskManagement.TaskStatus;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class OrchestratorTask : ITask
    {
        private const int MaxRetryCount = 3;
        private const int MaxRunningTaskCount = 50;
        private const int PollingFrequencyInSeconds = 3;

        private CreateBulkImportRequest _orchestratorInputData;
        private OrchestratorTaskContext _orchestratorTaskContext; // TODO : changed later
        private ITaskManager _taskManager;
        private IFhirDataBulkOperation _fhirDataBulkOperation;
        private IContextUpdater _contextUpdater;
        private IFhirRequestContextAccessor _contextAccessor;
        private ILogger<OrchestratorTask> _logger;
        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        public OrchestratorTask(
            CreateBulkImportRequest orchestratorInputData,
            OrchestratorTaskContext orchestratorTaskContext,
            ITaskManager taskManager,
            IContextUpdater contextUpdater,
            IFhirRequestContextAccessor contextAccessor,
            IFhirDataBulkOperation fhirDataBulkOperation,
            ILoggerFactory loggerFactory)
        {
            _orchestratorInputData = orchestratorInputData;
            _orchestratorTaskContext = orchestratorTaskContext;
            _taskManager = taskManager;
            _contextUpdater = contextUpdater;
            _contextAccessor = contextAccessor;
            _fhirDataBulkOperation = fhirDataBulkOperation;
            _logger = loggerFactory.CreateLogger<OrchestratorTask>();
        }

        public string RunId { get; set; }

        public async Task<TaskResultData> ExecuteAsync()
        {
            var fhirRequestContext = new FhirRequestContext(
                    method: "Import",
                    uriString: _orchestratorInputData.RequestUri.ToString(),
                    baseUriString: _orchestratorInputData.InputSource.ToString(),
                    correlationId: RunId,
                    requestHeaders: new Dictionary<string, StringValues>(),
                    responseHeaders: new Dictionary<string, StringValues>())
            {
                IsBackgroundTask = true,
            };

            _contextAccessor.FhirRequestContext = fhirRequestContext;

            CancellationToken cancellationToken = _cancellationTokenSource.Token;

            try
            {
                if (_orchestratorTaskContext.Progress == OrchestratorTaskProgress.Check)
                {
                    // check ETag of input
                    await MoveForwardProgress(cancellationToken);
                }

                if (_orchestratorTaskContext.Progress == OrchestratorTaskProgress.Preprocess)
                {
                    // disable unclustered index
                    await MoveForwardProgress(cancellationToken);
                }

                if (_orchestratorTaskContext.Progress == OrchestratorTaskProgress.GenerateSubTaskRecords)
                {
                    GenerateSubTaskRecords();
                    await MoveForwardProgress(cancellationToken);
                }

                if (_orchestratorTaskContext.Progress == OrchestratorTaskProgress.CreateAndMonitorSubTask)
                {
                    await CreateAndMonitorSubTask(cancellationToken);
                    await MoveForwardProgress(cancellationToken);
                }

                if (_orchestratorTaskContext.Progress == OrchestratorTaskProgress.Postprocess)
                {
                    // re-enable unclustered index
                    await MoveForwardProgress(cancellationToken);
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

        private async Task MoveForwardProgress(CancellationToken cancellationToken)
        {
            _orchestratorTaskContext.Progress++;
            await _contextUpdater.UpdateContextAsync(JsonConvert.SerializeObject(_orchestratorTaskContext), cancellationToken);
        }

        private void GenerateSubTaskRecords()
        {
            // this is a first start, generate task record for every input
            if (_orchestratorTaskContext.SubTaskRecords.Count == 0)
            {
                foreach (var input in _orchestratorInputData.Input)
                {
                    var taskInputData = GenerateSubTaskInputFromInput(input);
                    var hash = taskInputData.ToLowerInvariant().ComputeHash();

                    _orchestratorTaskContext.SubTaskRecords[hash] = new OrchestratorSubTaskRecord
                    {
                        TaskId = Guid.NewGuid().ToString(),
                        TaskInputData = taskInputData,
                    };
                }
            }
        }

        private async Task CreateAndMonitorSubTask(CancellationToken cancellationToken)
        {
            var pendingTaskRecords = DrillPendingTasksToList();

            var runningTaskRecords = new List<OrchestratorSubTaskRecord>();

            while (pendingTaskRecords.Count > 0)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException();
                }

                if (runningTaskRecords.Count >= MaxRunningTaskCount)
                {
                    await Task.Delay(TimeSpan.FromSeconds(PollingFrequencyInSeconds));

                    runningTaskRecords = await MonitorRunningTaskRecords(runningTaskRecords, pendingTaskRecords, cancellationToken);
                }

                await CreateSubTaskIfNotExistOrFailed(pendingTaskRecords.Pop(), runningTaskRecords, pendingTaskRecords, cancellationToken);

                while (pendingTaskRecords.Count == 0 && runningTaskRecords.Count > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(PollingFrequencyInSeconds));

                    runningTaskRecords = await MonitorRunningTaskRecords(runningTaskRecords, pendingTaskRecords, cancellationToken);
                }
            }
        }

        private async Task<List<OrchestratorSubTaskRecord>> MonitorRunningTaskRecords(List<OrchestratorSubTaskRecord> runningTaskRecords, List<OrchestratorSubTaskRecord> pendingTaskRecords, CancellationToken cancellationToken)
        {
            var completedTaskRecordsWithResult = new List<(OrchestratorSubTaskRecord, string)>();
            var newRunningTaskRecords = new List<OrchestratorSubTaskRecord>();

            foreach (var taskRecord in runningTaskRecords)
            {
                var taskInfo = await _taskManager.GetTaskAsync(taskRecord.TaskId, cancellationToken);
                if (taskInfo.Status == TaskStatus.Completed)
                {
                    completedTaskRecordsWithResult.Add((taskRecord, taskInfo.Result));
                }
                else
                {
                    newRunningTaskRecords.Add(taskRecord);
                }
            }

            if (completedTaskRecordsWithResult.Count > 0)
            {
                runningTaskRecords = newRunningTaskRecords;
                await HandleCompletedTaskRecords(completedTaskRecordsWithResult, pendingTaskRecords, cancellationToken);
            }

            return runningTaskRecords;
        }

        private List<OrchestratorSubTaskRecord> DrillPendingTasksToList()
        {
            var pendingTaskRecords = new List<OrchestratorSubTaskRecord>();
            var allSubTaskRecords = _orchestratorTaskContext.SubTaskRecords.Values.ToList();
            foreach (var taskRecord in allSubTaskRecords)
            {
                if (!taskRecord.IsCompletedSuccessfully)
                {
                    pendingTaskRecords.Add(new OrchestratorSubTaskRecord
                    {
                        TaskId = taskRecord.TaskId,
                        TaskInputData = taskRecord.TaskInputData,
                    });
                }
            }

            return pendingTaskRecords;
        }

        private async Task CreateSubTaskIfNotExistOrFailed(
            OrchestratorSubTaskRecord taskRecord,
            List<OrchestratorSubTaskRecord> runningTaskRecords,
            List<OrchestratorSubTaskRecord> pendingTaskRecords,
            CancellationToken cancellationToken)
        {
            TaskInfo taskInfo = await _taskManager.GetTaskAsync(taskRecord.TaskId, cancellationToken);

            if (taskInfo == null)
            {
                taskInfo = GenerateTaskInfoFromRecord(taskRecord);
                await _taskManager.CreateTaskAsync(taskInfo, cancellationToken);
                runningTaskRecords.Add(taskRecord);
            }
            else
            {
                if (taskInfo.Status == TaskStatus.Completed)
                {
                    HandleCompletedTaskRecord(taskRecord, taskInfo.Result, pendingTaskRecords);
                    await _contextUpdater.UpdateContextAsync(JsonConvert.SerializeObject(_orchestratorTaskContext), cancellationToken);
                }
                else
                {
                    runningTaskRecords.Add(taskRecord);
                }
            }
        }

        private async Task HandleCompletedTaskRecords(
            List<(OrchestratorSubTaskRecord taskRecord, string result)> completedTaskRecordsWithResult,
            List<OrchestratorSubTaskRecord> pendingTaskRecords,
            CancellationToken cancellationToken)
        {
            foreach (var tasRecordWithResult in completedTaskRecordsWithResult)
            {
                HandleCompletedTaskRecord(tasRecordWithResult.taskRecord, tasRecordWithResult.result, pendingTaskRecords);
            }

            // update context here to make sure DB contains latest taskId before running
            await _contextUpdater.UpdateContextAsync(JsonConvert.SerializeObject(_orchestratorTaskContext), cancellationToken);
        }

        private void HandleCompletedTaskRecord(OrchestratorSubTaskRecord taskRecord, string taskResult, List<OrchestratorSubTaskRecord> pendingTaskRecords)
        {
            var hash = taskRecord.TaskInputData.ToLowerInvariant().ComputeHash();
            var result = JsonConvert.DeserializeObject<TaskResultData>(taskResult).Result;

            if (result == TaskResult.Success)
            {
                _orchestratorTaskContext.SubTaskRecords[hash].IsCompletedSuccessfully = true;
            }
            else
            {
                HandleFailedTaskRecord(taskRecord, hash, pendingTaskRecords);
            }
        }

        private void HandleFailedTaskRecord(OrchestratorSubTaskRecord taskRecord, string hash, List<OrchestratorSubTaskRecord> pendingTaskRecords)
        {
            if (taskRecord.RetryCount >= MaxRetryCount)
            {
                throw new Exception($"Task with Input {taskRecord.TaskInputData} failed after retry {MaxRetryCount} times, latest task Id is {taskRecord.TaskId}");
            }

            _orchestratorTaskContext.SubTaskRecords[hash].TaskId = Guid.NewGuid().ToString();
            _orchestratorTaskContext.SubTaskRecords[hash].RetryCount++;
            pendingTaskRecords.Insert(0, _orchestratorTaskContext.SubTaskRecords[hash]);
        }

        private TaskInfo GenerateTaskInfoFromRecord(OrchestratorSubTaskRecord subTaskRecord)
        {
            return new TaskInfo
            {
                TaskId = subTaskRecord.TaskId,
                InputData = subTaskRecord.TaskInputData,
                TaskTypeId = ImportTask.ResourceImportTaskId,
                QueueId = "0",
            };
        }

        private static string GenerateSubTaskInputFromInput(BulkImportRequestInput input)
        {
            return input.Etag;
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
