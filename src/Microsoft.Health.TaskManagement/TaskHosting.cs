// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.Health.TaskManagement
{
    public class TaskHosting
    {
        private IQueueClient _queueClient;
        private ITaskFactory _taskFactory;
        private ILogger<TaskHosting> _logger;
        private ConcurrentDictionary<long, Func<Task>> _activeTasksNeedKeepAlive = new ConcurrentDictionary<long, Func<Task>>();

        public TaskHosting(IQueueClient queueClient, ITaskFactory taskFactory, ILogger<TaskHosting> logger)
        {
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            EnsureArg.IsNotNull(taskFactory, nameof(taskFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _queueClient = queueClient;
            _taskFactory = taskFactory;
            _logger = logger;
        }

        public int PollingFrequencyInSeconds { get; set; } = Constants.DefaultPollingFrequencyInSeconds;

        public short MaxRunningTaskCount { get; set; } = Constants.DefaultMaxRunningTaskCount;

        public int TaskHeartbeatTimeoutThresholdInSeconds { get; set; } = Constants.DefaultTaskHeartbeatTimeoutThresholdInSeconds;

        public int TaskHeartbeatIntervalInSeconds { get; set; } = Constants.DefaultTaskHeartbeatIntervalInSeconds;

        public byte StartPartitionId { get; set; }

        public async Task StartAsync(byte queueType, string workerName, CancellationTokenSource cancellationToken)
        {
            using CancellationTokenSource keepAliveCancellationToken = new CancellationTokenSource();
            Task keepAliveTask = KeepAliveTasksAsync(keepAliveCancellationToken.Token);

            await PullAndProcessTasksAsync(queueType, workerName, cancellationToken.Token);

            keepAliveCancellationToken.Cancel();
            await keepAliveTask;
        }

        private async Task PullAndProcessTasksAsync(byte queueType, string workerName, CancellationToken cancellationToken)
        {
            List<Task> runningTasks = new List<Task>();

            while (!cancellationToken.IsCancellationRequested)
            {
                Task intervalDelayTask = Task.Delay(TimeSpan.FromSeconds(PollingFrequencyInSeconds), CancellationToken.None);

                if (runningTasks.Count >= MaxRunningTaskCount)
                {
                    _ = await Task.WhenAny(runningTasks.ToArray());
                    runningTasks.RemoveAll(t => t.IsCompleted);
                }

                TaskInfo nextTask = null;
                if (_queueClient.IsInitialized())
                {
                    try
                    {
                        nextTask = await _queueClient.DequeueAsync(queueType, StartPartitionId, workerName, TaskHeartbeatTimeoutThresholdInSeconds, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to pull new tasks.");
                    }
                }

                if (nextTask != null)
                {
                    runningTasks.Add(ExecuteTaskAsync(nextTask, cancellationToken));
                }
                else
                {
                    await intervalDelayTask;
                }
            }

            try
            {
                await Task.WhenAll(runningTasks.ToArray());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Task failed to execute");
            }
        }

        private async Task ExecuteTaskAsync(TaskInfo taskInfo, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(taskInfo, nameof(taskInfo));
            using var taskCancellationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            ITask task = _taskFactory.Create(taskInfo);

            if (task == null)
            {
                _logger.LogWarning("Not supported task type: {taskTypeId}", taskInfo.TaskTypeId);
                return;
            }

            try
            {
                try
                {
                    if (taskInfo.CancelRequested && !taskCancellationToken.IsCancellationRequested)
                    {
                        // For cancelled task, try to execute it for potential cleanup.
                        taskCancellationToken.Cancel();
                    }

                    Progress<string> progress = new Progress<string>((result) =>
                    {
                        taskInfo.Result = result;
                    });
                    Task<string> runningTask = Task.Run(() => task.ExecuteAsync(progress, taskCancellationToken.Token));
                    _activeTasksNeedKeepAlive[taskInfo.Id] = () => KeepAliveSingleTaskAsync(taskInfo, taskCancellationToken);

                    taskInfo.Result = await runningTask;
                }
                catch (RetriableTaskException ex)
                {
                    _logger.LogError(ex, "Task {taskId} failed with retriable exception.", taskInfo.TaskId);

                    // Not complete the task for retriable exception.
                    return;
                }
                catch (TaskExecutionException ex)
                {
                    _logger.LogError(ex, "Task {taskId} failed.", taskInfo.TaskId);
                    taskInfo.Result = JsonConvert.SerializeObject(ex.Error);
                    taskInfo.Status = TaskStatus.Failed;

                    try
                    {
                        await _queueClient.CompleteTaskAsync(taskInfo, ex.RequestCancellationOnFailure, CancellationToken.None);
                    }
                    catch (Exception completeEx)
                    {
                        _logger.LogError(completeEx, "Task {taskId} failed to complete.", taskInfo.TaskId);
                    }

                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Task {taskId} failed.", taskInfo.TaskId);

                    object error = new { message = ex.Message };
                    taskInfo.Result = JsonConvert.SerializeObject(error);
                    taskInfo.Status = TaskStatus.Failed;

                    try
                    {
                        await _queueClient.CompleteTaskAsync(taskInfo, true, CancellationToken.None);
                    }
                    catch (Exception completeEx)
                    {
                        _logger.LogError(completeEx, "Task {taskId} failed to complete.", taskInfo.TaskId);
                    }

                    return;
                }

                try
                {
                    taskInfo.Status = TaskStatus.Completed;
                    await _queueClient.CompleteTaskAsync(taskInfo, true, CancellationToken.None);
                    _logger.LogInformation("Task {taskId} completed.", taskInfo.TaskId);
                }
                catch (Exception completeEx)
                {
                    _logger.LogError(completeEx, "Task {taskId} failed to complete.", taskInfo.TaskId);
                }
            }
            finally
            {
                _activeTasksNeedKeepAlive.Remove(taskInfo.Id, out _);
            }
        }

        private async Task KeepAliveSingleTaskAsync(TaskInfo taskInfo, CancellationTokenSource taskCancellationToken)
        {
            try
            {
                bool shouldCancel = false;
                try
                {
                    TaskInfo keepAliveResult = await _queueClient.KeepAliveTaskAsync(taskInfo, CancellationToken.None);
                    shouldCancel |= keepAliveResult.CancelRequested;
                }
                catch (TaskNotExistException notExistEx)
                {
                    _logger.LogError(notExistEx, "Task {taskId} not exist or runid not match.", taskInfo.Id);
                    shouldCancel = true;
                }

                if (shouldCancel && !taskCancellationToken.IsCancellationRequested)
                {
                    taskCancellationToken.Cancel();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to keep alive on task {taskId}", taskInfo.Id);
            }
        }

        private async Task KeepAliveTasksAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Start to keep alive task message.");

            while (!cancellationToken.IsCancellationRequested)
            {
                Task intervalDelayTask = Task.Delay(TimeSpan.FromSeconds(TaskHeartbeatIntervalInSeconds), CancellationToken.None);
                KeyValuePair<long, Func<Task>>[] activeTaskRecords = _activeTasksNeedKeepAlive.ToArray();

                foreach ((long taskId, Func<Task> keepAliveFunc) in activeTaskRecords)
                {
                    try
                    {
                        await keepAliveFunc();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to keep alive on task {taskId}", taskId);
                    }
                }

                await intervalDelayTask;
            }

            _logger.LogInformation("Stop to keep alive task message.");
        }
    }
}
