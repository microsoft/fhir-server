// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.TaskManagement.UnitTests
{
    public class TestQueueClient : IQueueClient
    {
        private List<TaskInfo> taskInfos = new List<TaskInfo>();
        private long largestId = 1;

        public Action DequeueFaultAction { get; set; }

        public Action HeartbeatFaultAction { get; set; }

        public Action CompleteFaultAction { get; set; }

        public Func<TestQueueClient, long, TaskInfo> GetTaskByIdFunc { get; set; }

        public List<TaskInfo> TaskInfos
        {
            get { return taskInfos; }
        }

        public Task CancelTaskByGroupIdAsync(byte queueType, long groupId, CancellationToken cancellationToken)
        {
            foreach (TaskInfo taskInfo in taskInfos.Where(t => t.GroupId == groupId))
            {
                if (taskInfo.Status == TaskStatus.Created)
                {
                    taskInfo.Status = TaskStatus.Cancelled;
                }

                if (taskInfo.Status == TaskStatus.Running)
                {
                    taskInfo.CancelRequested = true;
                }
            }

            return Task.CompletedTask;
        }

        public Task CancelTaskByIdAsync(byte queueType, long taskId, CancellationToken cancellationToken)
        {
            foreach (TaskInfo taskInfo in taskInfos.Where(t => t.Id == taskId))
            {
                if (taskInfo.Status == TaskStatus.Created)
                {
                    taskInfo.Status = TaskStatus.Cancelled;
                }

                if (taskInfo.Status == TaskStatus.Running)
                {
                    taskInfo.CancelRequested = true;
                }
            }

            return Task.CompletedTask;
        }

        public async Task CompleteTaskAsync(TaskInfo taskInfo, bool requestCancellationOnFailure, CancellationToken cancellationToken)
        {
            CompleteFaultAction?.Invoke();

            TaskInfo taskInfoStore = taskInfos.FirstOrDefault(t => t.Id == taskInfo.Id);
            taskInfoStore.Status = taskInfo.Status;
            taskInfoStore.Result = taskInfo.Result;

            if (requestCancellationOnFailure && taskInfo.Status == TaskStatus.Failed)
            {
                await CancelTaskByGroupIdAsync(taskInfo.QueueType, taskInfo.GroupId, cancellationToken);
            }
        }

        public Task<TaskInfo> DequeueAsync(byte queueType, byte startPartitionId, string worker, int heartbeatTimeoutSec, CancellationToken cancellationToken)
        {
            DequeueFaultAction?.Invoke();

            TaskInfo task = taskInfos.FirstOrDefault(t => t.Status == TaskStatus.Created || (t.Status == TaskStatus.Running && (DateTime.Now - t.HeartbeatDateTime) > TimeSpan.FromSeconds(heartbeatTimeoutSec)));
            if (task != null)
            {
                task.Status = TaskStatus.Running;
                task.HeartbeatDateTime = DateTime.Now;
            }

            return Task.FromResult(task);
        }

        public Task<IEnumerable<TaskInfo>> EnqueueAsync(byte queueType, string[] definitions, long? groupId, bool forceOneActiveJobGroup, bool isCompleted, CancellationToken cancellationToken)
        {
            List<TaskInfo> result = new List<TaskInfo>();

            long gId = groupId ?? largestId++;
            foreach (string definition in definitions)
            {
                if (taskInfos.Any(t => t.Definition.Equals(definition)))
                {
                    result.Add(taskInfos.First(t => t.Definition.Equals(definition)));
                    continue;
                }

                result.Add(new TaskInfo()
                {
                    Definition = definition,
                    Id = largestId,
                    GroupId = gId,
                    Status = TaskStatus.Created,
                    HeartbeatDateTime = DateTime.Now,
                });
                largestId++;
            }

            taskInfos.AddRange(result);
            return Task.FromResult<IEnumerable<TaskInfo>>(result);
        }

        public Task<IEnumerable<TaskInfo>> GetTaskByGroupIdAsync(byte queueType, long groupId, bool returnDefinition, CancellationToken cancellationToken)
        {
            IEnumerable<TaskInfo> result = taskInfos.Where(t => t.GroupId == groupId);
            return Task.FromResult<IEnumerable<TaskInfo>>(result);
        }

        public Task<TaskInfo> GetTaskByIdAsync(byte queueType, long taskId, bool returnDefinition, CancellationToken cancellationToken)
        {
            if (GetTaskByIdFunc != null)
            {
                return Task.FromResult(GetTaskByIdFunc(this, taskId));
            }

            TaskInfo result = taskInfos.FirstOrDefault(t => t.Id == taskId);
            return Task.FromResult(result);
        }

        public Task<IEnumerable<TaskInfo>> GetTaskByIdsAsync(byte queueType, long[] taskIds, bool returnDefinition, CancellationToken cancellationToken)
        {
            IEnumerable<TaskInfo> result = taskInfos.Where(t => taskIds.Contains(t.Id));
            return Task.FromResult<IEnumerable<TaskInfo>>(result);
        }

        public bool IsInitialized()
        {
            return true;
        }

        public Task<TaskInfo> KeepAliveTaskAsync(TaskInfo taskInfo, CancellationToken cancellationToken)
        {
            HeartbeatFaultAction?.Invoke();

            TaskInfo task = taskInfos.FirstOrDefault(t => t.Id == taskInfo.Id);
            if (task == null)
            {
                throw new TaskNotExistException("not exist");
            }

            task.HeartbeatDateTime = DateTime.Now;
            task.Result = taskInfo.Result;

            return Task.FromResult(task);
        }
    }
}
