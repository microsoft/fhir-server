// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.TaskManagement
{
    public class TaskHosting
    {
        private ITaskConsumer _consumer;
        private ITaskFactory _taskFactory;
        private List<(TaskInfo, Task)> _runningTasks = new List<(TaskInfo, Task)>();

        public TaskHosting(ITaskConsumer consumer, ITaskFactory taskFactory)
        {
            _consumer = consumer;
            _taskFactory = taskFactory;
        }

        public int PollingFrequencyInSeconds { get; set; } = Constants.DefaultPollingFrequencyInSeconds;

        public int MaxRunningTaskCount { get; set; } = Constants.MaxRunningTaskCount;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Task intervalDelayTask = Task.Delay(TimeSpan.FromSeconds(PollingFrequencyInSeconds));

                if (_runningTasks.Count < MaxRunningTaskCount)
                {
                    IReadOnlyCollection<TaskInfo> nextTasks = await _consumer.GetNextMessagesAsync(MaxRunningTaskCount - _runningTasks.Count);
                    foreach (TaskInfo taskInfo in nextTasks)
                    {
                    }
                }

                await intervalDelayTask;
            }
        }
    }
}
