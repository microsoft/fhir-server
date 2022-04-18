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

namespace Microsoft.Health.TaskManagement
{
    public class TaskHosting
    {
        private ITaskConsumer _consumer;
        private ITaskFactory _taskFactory;
        private ILogger<TaskHosting> _logger;
        private ConcurrentDictionary<string, ITask> _activeTaskRecordsForKeepAlive = new ConcurrentDictionary<string, ITask>();

        public TaskHosting(ITaskConsumer consumer, ITaskFactory taskFactory, ILogger<TaskHosting> logger)
        {
            EnsureArg.IsNotNull(consumer, nameof(consumer));
            EnsureArg.IsNotNull(taskFactory, nameof(taskFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _consumer = consumer;
            _taskFactory = taskFactory;
            _logger = logger;
        }

        public int PollingFrequencyInSeconds { get; set; } = Constants.DefaultPollingFrequencyInSeconds;

        public short MaxRunningTaskCount { get; set; } = Constants.DefaultMaxRunningTaskCount;

        public int TaskHeartbeatTimeoutThresholdInSeconds { get; set; } = Constants.DefaultTaskHeartbeatTimeoutThresholdInSeconds;

        public int TaskHeartbeatIntervalInSeconds { get; set; } = Constants.DefaultTaskHeartbeatIntervalInSeconds;

        public async Task StartAsync(CancellationTokenSource cancellationToken)
        {
            using CancellationTokenSource keepAliveCancellationToken = new CancellationTokenSource();
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
                Task intervalDelayTask = Task.Delay(TimeSpan.FromSeconds(PollingFrequencyInSeconds), CancellationToken.None);

                if (runningTasks.Count >= MaxRunningTaskCount)
                {
                    _ = await Task.WhenAny(runningTasks.ToArray());
                    runningTasks.RemoveAll(t => t.IsCompleted);
                }

                TaskInfo nextTask = null;
                try
                {
                    nextTask = await _consumer.GetNextMessagesAsync(TaskHeartbeatTimeoutThresholdInSeconds, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to pull new tasks.");
                }

                if (nextTask != null)
                {
                    runningTasks.Add(ExecuteTaskAsync(nextTask, cancellationToken));
                }

                await intervalDelayTask;
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

            using ITask task = _taskFactory.Create(taskInfo);
            task.RunId = task.RunId ?? taskInfo.RunId;

            if (task == null)
            {
                _logger.LogWarning("Not supported task type: {TaskTypeId}", taskInfo.TaskTypeId);
                return;
            }

            TaskResultData result = null;
            try
            {
                try
                {
                    if (taskInfo.IsCanceled)
                    {
                        // For cancelled task, try to execute it for potential cleanup.
                        task.Cancel();
                    }

                    Task<TaskResultData> runningTask = Task.Run(() => task.ExecuteAsync());
                    _activeTaskRecordsForKeepAlive[taskInfo.TaskId] = task;

                    result = await runningTask;
                }
                catch (RetriableTaskException ex)
                {
                    _logger.LogError(ex, "Task {TaskId} failed with retriable exception.", taskInfo.TaskId);

                    try
                    {
                        await _consumer.ResetAsync(taskInfo.TaskId, new TaskResultData(TaskResult.Fail, ex.Message), taskInfo.RunId, cancellationToken);
                    }
                    catch (Exception resetEx)
                    {
                        _logger.LogError(resetEx, "Task {TaskId} failed to reset.", taskInfo.TaskId);
                    }

                    // Not complete the task for retriable exception.
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Task {TaskId} failed.", taskInfo.TaskId);
                    result = new TaskResultData(TaskResult.Fail, ex.Message);
                }

                try
                {
                    await _consumer.CompleteAsync(taskInfo.TaskId, result, task.RunId, cancellationToken);
                    _logger.LogInformation("Task {TaskId} completed.", taskInfo.TaskId);
                }
                catch (Exception completeEx)
                {
                    _logger.LogError(completeEx, "Task {TaskId} failed to complete.", taskInfo.TaskId);
                }
            }
            finally
            {
                _activeTaskRecordsForKeepAlive.Remove(taskInfo.TaskId, out _);
            }
        }

        private async Task KeepAliveTasksAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Start to keep alive task message.");

            while (!cancellationToken.IsCancellationRequested)
            {
                Task intervalDelayTask = Task.Delay(TimeSpan.FromSeconds(TaskHeartbeatIntervalInSeconds), CancellationToken.None);
                KeyValuePair<string, ITask>[] activeTaskRecords = _activeTaskRecordsForKeepAlive.ToArray();

                foreach ((string taskId, ITask task) in activeTaskRecords)
                {
                    try
                    {
                        bool shouldCancel = false;
                        try
                        {
                            TaskInfo taskInfo = await _consumer.KeepAliveAsync(taskId, task.RunId, cancellationToken);
                            shouldCancel |= taskInfo.IsCanceled;
                        }
                        catch (TaskNotExistException notExistEx)
                        {
                            _logger.LogError(notExistEx, "Task {TaskId} not exist or runid not match.", taskId);
                            shouldCancel = true;
                        }

                        if (shouldCancel)
                        {
                            try
                            {
                                task.Cancel();
                            }
                            catch (OperationCanceledException operationCanceledEx)
                            {
                                _logger.LogError(operationCanceledEx, "Task {TaskId} was canceled.", taskId);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to keep alive on task {TaskId}", taskId);
                    }
                }

                await intervalDelayTask;
            }

            _logger.LogInformation("Stop to keep alive task message.");
        }
    }
}
