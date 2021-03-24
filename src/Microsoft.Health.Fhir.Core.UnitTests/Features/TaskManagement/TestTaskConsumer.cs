// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.TaskManagement;
using Newtonsoft.Json;
using TaskStatus = Microsoft.Health.Fhir.Core.Features.TaskManagement.TaskStatus;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.TaskManagement
{
    public class TestTaskConsumer : ITaskConsumer
    {
        private int _maxRetryCount;
        private Dictionary<string, TaskInfo> _taskInfos;
        private HashSet<string> _taskIds = new HashSet<string>();
        private Action<string> _faultInjectionAction;

        public TestTaskConsumer(TaskInfo[] taskInfos, int maxRetryCount = 3, Action<string> faultInjectionAction = null)
        {
            _taskInfos = taskInfos.ToDictionary(t => t.TaskId, t => t);
            _maxRetryCount = maxRetryCount;
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

        public Task<TaskInfo> CompleteAsync(string taskId, TaskResultData result)
        {
            _faultInjectionAction?.Invoke("CompleteAsync");

            TaskInfo task = _taskInfos[taskId];
            task.Status = TaskStatus.Completed;
            task.Result = JsonConvert.SerializeObject(result);

            return Task.FromResult<TaskInfo>(task);
        }

        public Task<IReadOnlyCollection<TaskInfo>> GetNextMessagesAsync(int count, int taskHeartbeatTimeoutThresholdInSeconds)
        {
            _faultInjectionAction?.Invoke("GetNextMessagesAsync");

            IReadOnlyCollection<TaskInfo> tasksInQueue = _taskInfos.Values
                                                                .Where(t => t.Status != TaskStatus.Completed)
                                                                .Where(t => t.Status != TaskStatus.Running || DateTime.Now - t.HeartbeatDateTime > TimeSpan.FromSeconds(taskHeartbeatTimeoutThresholdInSeconds))
                                                                .OrderBy(t => t.HeartbeatDateTime)
                                                                .Take(count)
                                                                .ToList();

            foreach (TaskInfo taskInfo in tasksInQueue)
            {
                taskInfo.Status = TaskStatus.Running;
            }

            return Task.FromResult<IReadOnlyCollection<TaskInfo>>(tasksInQueue);
        }

        public Task<TaskInfo> KeepAliveAsync(string taskId)
        {
            _faultInjectionAction?.Invoke("KeepAliveAsync");

            TaskInfo taskInfo = _taskInfos[taskId];
            taskInfo.HeartbeatDateTime = DateTime.Now;

            return Task.FromResult<TaskInfo>(taskInfo);
        }

        public Task ResetAsync(string taskId, TaskResultData result)
        {
            _faultInjectionAction?.Invoke("ResetAsync");

            _taskInfos[taskId].Result = JsonConvert.SerializeObject(result);
            _taskInfos[taskId].RetryCount += 1;

            if (_taskInfos[taskId].RetryCount > _maxRetryCount)
            {
                _taskInfos[taskId].Status = TaskStatus.Completed;
            }
            else
            {
                _taskInfos[taskId].Status = TaskStatus.Queued;
            }

            return Task.CompletedTask;
        }
    }
}
