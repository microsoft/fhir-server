// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.BulkImportDemoWorker
{
    public class TaskHosting
    {
        private ITaskConsumer _consumer;
        private int _maxRunningTaskCount;
        private Func<TaskInfo, ITask> _taskFactory;

        private List<TaskWrapper> _runningTasks = new List<TaskWrapper>();

        public TaskHosting(ITaskConsumer consumer, Func<TaskInfo, ITask> taskFactory, int maxRunningTaskCount)
        {
            _consumer = consumer;
            _taskFactory = taskFactory;
            _maxRunningTaskCount = maxRunningTaskCount;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                System.Threading.Tasks.Task intervalDelayTask = System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(10));

                await UpdateRunningTasksAsync();
                if (_runningTasks.Count < _maxRunningTaskCount)
                {
                    IReadOnlyCollection<TaskInfo> nextTasks = await _consumer.GetNextMessagesAsync(_maxRunningTaskCount - _runningTasks.Count);
                    foreach (TaskInfo taskInfo in nextTasks)
                    {
                        _runningTasks.Add(new TaskWrapper()
                        {
                            TaskInfo = taskInfo,
                            RunningTask = StartTaskAsync(taskInfo, cancellationToken),
                        });
                    }
                }

                await intervalDelayTask;
            }
        }

        private async Task StartTaskAsync(TaskInfo taskInfo, CancellationToken cancellationToken)
        {
            ITask task = _taskFactory(taskInfo);
            Progress<string> progress = new Progress<string>(
                (context) =>
                {
                    taskInfo.TaskContext = context;
                });
            await task.ExecuteAsync(progress, cancellationToken);
        }

        private async Task UpdateRunningTasksAsync()
        {
            List<TaskWrapper> completedTasks = new List<TaskWrapper>();
            foreach (var runningTask in _runningTasks)
            {
                if (runningTask.RunningTask.IsCompleted)
                {
                    await _consumer.CompleteAsync(runningTask.TaskInfo);
                    completedTasks.Add(runningTask);
                }
                else
                {
                    await _consumer.UpdateContextAsync(runningTask.TaskInfo);
                }
            }

            _runningTasks.RemoveAll(t => completedTasks.Contains(t));
        }

        public async Task StopAndWaitCompleteAsync()
        {
            foreach (Task runningTask in _runningTasks.Select(t => t.RunningTask).ToArray())
            {
                await runningTask;
            }
        }
    }
}
