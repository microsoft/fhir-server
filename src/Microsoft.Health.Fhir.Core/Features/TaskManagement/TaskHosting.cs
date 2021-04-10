// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Exceptions;

namespace Microsoft.Health.Fhir.Core.Features.TaskManagement
{
    public class TaskHosting
    {
        private object _syncRoot = new object();

        private ITaskConsumer _consumer;
        private ITaskFactory _taskFactory;
        private ILogger<TaskHosting> _logger;
        private Dictionary<string, ITask> _activeTaskRecordsForKeepAlive = new Dictionary<string, ITask>();

        public TaskHosting(ITaskConsumer consumer, ITaskFactory taskFactory, ILogger<TaskHosting> logger)
        {
            _consumer = consumer;
            _taskFactory = taskFactory;
            _logger = logger;
        }

        public int PollingFrequencyInSeconds { get; set; } = Constants.DefaultPollingFrequencyInSeconds;

        public short MaxRunningTaskCount { get; set; } = Constants.DefaultMaxRunningTaskCount;

        public short MaxRetryCount { get; set; } = Constants.DefaultMaxRetryCount;

        public int TaskHeartbeatTimeoutThresholdInSeconds { get; set; } = Constants.DefaultTaskHeartbeatTimeoutThresholdInSeconds;

        public int TaskHeartbeatIntervalInSeconds { get; set; } = Constants.DefaultTaskHeartbeatIntervalInSeconds;

        public async Task StartAsync(CancellationTokenSource cancellationToken)
        {
            CancellationTokenSource keepAliveCancellationToken = new CancellationTokenSource();
            Task keepAliveTask = KeepAliveTasksAsync(keepAliveCancellationToken.Token);

            await PullAndProcessTasksAsync(cancellationToken.Token);

            keepAliveCancellationToken.Cancel();
            await keepAliveTask;
        }

        private async Task PullAndProcessTasksAsync(CancellationToken cancellationToken)
        {
            List<Task> runningTasks = new List<Task>();

            while (!cancellationToken.IsCancellationRequested)
            {
                Task intervalDelayTask = Task.Delay(TimeSpan.FromSeconds(PollingFrequencyInSeconds));

                if (runningTasks.Count >= MaxRunningTaskCount)
                {
                    _ = Task.WhenAny(runningTasks.ToArray());
                    runningTasks.RemoveAll(t => t.IsCompleted);
                }

                IReadOnlyCollection<TaskInfo> nextTasks = null;
                try
                {
                    nextTasks = await _consumer.GetNextMessagesAsync((short)(MaxRunningTaskCount - runningTasks.Count), TaskHeartbeatTimeoutThresholdInSeconds, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to pull new tasks.");
                }

                if (nextTasks != null && nextTasks.Count > 0)
                {
                    foreach (TaskInfo taskInfo in nextTasks)
                    {
                        runningTasks.Add(ExecuteTaskAsync(taskInfo, cancellationToken));
                    }
                }

                await intervalDelayTask;
            }

            try
            {
                Task.WaitAll(runningTasks.ToArray());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Task failed to execute");
            }
        }

        private async Task ExecuteTaskAsync(TaskInfo taskInfo, CancellationToken cancellationToken)
        {
            using ITask task = _taskFactory.Create(taskInfo);

            if (task == null)
            {
                _logger.LogWarning($"Not supported task type: {taskInfo.TaskTypeId}");
                return;
            }

            task.RunId = taskInfo.RunId;
            TaskResultData result = null;
            try
            {
                Task<TaskResultData> runningTask = task.ExecuteAsync();

                lock (_syncRoot)
                {
                    _activeTaskRecordsForKeepAlive[taskInfo.TaskId] = task;
                }

                result = await runningTask;
            }
            catch (RetriableTaskException ex)
            {
                _logger.LogError(ex, $"Task {taskInfo.TaskId} failed with retriable exception.");

                try
                {
                    await _consumer.ResetAsync(taskInfo.TaskId, new TaskResultData(TaskResult.Fail, ex.Message), taskInfo.RunId, MaxRetryCount, cancellationToken);
                }
                catch (Exception resetEx)
                {
                    _logger.LogError(resetEx, $"Task {taskInfo.TaskId} failed to reset.");
                }

                // Not complete the task for retriable exception.
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Task {taskInfo.TaskId} failed. ");
                result = new TaskResultData(TaskResult.Fail, ex.Message);
            }
            finally
            {
                lock (_syncRoot)
                {
                    _activeTaskRecordsForKeepAlive.Remove(taskInfo.TaskId);
                }
            }

            try
            {
                await _consumer.CompleteAsync(taskInfo.TaskId, result, task.RunId, cancellationToken);
                _logger.LogInformation($"Task {taskInfo.TaskId} completed.");
            }
            catch (Exception completeEx)
            {
                _logger.LogError(completeEx, $"Task {taskInfo.TaskId} failed to complete.");
            }
        }

        private async Task KeepAliveTasksAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation($"Start to keep alive task message.");

            while (!cancellationToken.IsCancellationRequested)
            {
                Task intervalDelayTask = Task.Delay(TaskHeartbeatIntervalInSeconds);

                KeyValuePair<string, ITask>[] activeTaskRecords = null;
                lock (_syncRoot)
                {
                    activeTaskRecords = _activeTaskRecordsForKeepAlive.ToArray();
                }

                foreach ((string taskId, ITask task) in activeTaskRecords)
                {
                    try
                    {
                        if (task.IsCancelling())
                        {
                            continue;
                        }

                        bool shouldCancel = false;
                        try
                        {
                            TaskInfo taskInfo = await _consumer.KeepAliveAsync(taskId, task.RunId, cancellationToken);
                            shouldCancel |= taskInfo.IsCanceled;
                        }
                        catch (TaskNotExistException notExistEx)
                        {
                            _logger.LogError(notExistEx, $"Task {taskId} not exist or runid not match.");
                            shouldCancel = true;
                        }

                        if (shouldCancel)
                        {
                            task.Cancel();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Failed to keep alive on task {taskId}");
                    }
                }

                await intervalDelayTask;
            }

            _logger.LogInformation($"Stop to keep alive task message.");
        }
    }
}
