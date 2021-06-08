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
using Newtonsoft.Json;

namespace Microsoft.Health.TaskManagement.UnitTests
{
    public class TestTaskConsumer : ITaskConsumer
    {
        private Dictionary<string, TaskInfo> _taskInfos;
        private HashSet<string> _taskIds = new HashSet<string>();
        private Action<string> _faultInjectionAction;

        public TestTaskConsumer(TaskInfo[] taskInfos, Action<string> faultInjectionAction = null)
        {
            _taskInfos = taskInfos.ToDictionary(t => t.TaskId, t => t);
            _faultInjectionAction = faultInjectionAction;

            foreach (TaskInfo t in _taskInfos.Values)
            {
                if (t.Status == null)
                {
                    t.Status = TaskStatus.Queued;
                }

                t.HeartbeatDateTime = DateTime.Now;
            }
        }

        public Task<TaskInfo> CompleteAsync(string taskId, TaskResultData result, string runId, CancellationToken cancellationToken)
        {
            _faultInjectionAction?.Invoke(nameof(CompleteAsync));

            TaskInfo task = _taskInfos[taskId];
            TaskInfo taskInfo = _taskInfos[taskId];
            if (!runId.Equals(taskInfo.RunId))
            {
                throw new TaskNotExistException("Task not exist");
            }

            task.Status = TaskStatus.Completed;
            task.Result = JsonConvert.SerializeObject(result);

            return Task.FromResult<TaskInfo>(task);
        }

        public Task<IReadOnlyCollection<TaskInfo>> GetNextMessagesAsync(short count, int taskHeartbeatTimeoutThresholdInSeconds, CancellationToken cancellationToken)
        {
            _faultInjectionAction?.Invoke(nameof(GetNextMessagesAsync));
            Ensure.Comparable.IsGt<short>(count, 0, nameof(count));

            IReadOnlyCollection<TaskInfo> tasksInQueue = _taskInfos.Values
                                                                .Where(t => t.Status != TaskStatus.Completed)
                                                                .Where(t => t.Status != TaskStatus.Running || DateTime.Now - t.HeartbeatDateTime > TimeSpan.FromSeconds(taskHeartbeatTimeoutThresholdInSeconds))
                                                                .OrderBy(t => t.HeartbeatDateTime)
                                                                .Take(count)
                                                                .ToList();

            foreach (TaskInfo taskInfo in tasksInQueue)
            {
                taskInfo.Status = TaskStatus.Running;
                taskInfo.RunId = Guid.NewGuid().ToString();
            }

            return Task.FromResult<IReadOnlyCollection<TaskInfo>>(tasksInQueue);
        }

        public Task<TaskInfo> KeepAliveAsync(string taskId, string runId, CancellationToken cancellationToken)
        {
            _faultInjectionAction?.Invoke(nameof(KeepAliveAsync));

            TaskInfo taskInfo = _taskInfos[taskId];
            if (!runId.Equals(taskInfo.RunId))
            {
                throw new TaskNotExistException("Task not exist");
            }

            taskInfo.HeartbeatDateTime = DateTime.Now;

            return Task.FromResult<TaskInfo>(taskInfo);
        }

        public Task<TaskInfo> ResetAsync(string taskId, TaskResultData result, string runId, CancellationToken cancellationToken)
        {
            _faultInjectionAction?.Invoke(nameof(ResetAsync));

            TaskInfo taskInfo = _taskInfos[taskId];
            if (!runId.Equals(taskInfo.RunId))
            {
                throw new TaskNotExistException("Task not exist");
            }

            taskInfo.Result = JsonConvert.SerializeObject(result);
            taskInfo.RetryCount += 1;

            if (taskInfo.RetryCount > taskInfo.MaxRetryCount)
            {
                taskInfo.Status = TaskStatus.Completed;
            }
            else
            {
                taskInfo.Status = TaskStatus.Queued;
            }

            return Task.FromResult<TaskInfo>(taskInfo);
        }
    }
}
