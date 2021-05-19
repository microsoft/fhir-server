// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.TaskManagement;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Import
{
    public class TestTaskManager : ITaskManager
    {
        public TestTaskManager(Func<TaskInfo, TaskInfo> getTaskInfoFunc)
        {
            GetTaskInfoFunc = getTaskInfoFunc;
        }

        public List<TaskInfo> TaskInfos { get; set; } = new List<TaskInfo>();

        public Func<TaskInfo, TaskInfo> GetTaskInfoFunc { get; set; }

        public Task<TaskInfo> CancelTaskAsync(string taskId, CancellationToken cancellationToken)
        {
            TaskInfo taskInfo = TaskInfos.FirstOrDefault(t => taskId.Equals(t.TaskId));
            taskInfo.IsCanceled = true;

            return Task.FromResult(taskInfo);
        }

        public Task<TaskInfo> CreateTaskAsync(TaskInfo task, CancellationToken cancellationToken)
        {
            TaskInfos.Add(task);

            return Task.FromResult(task);
        }

        public Task<IReadOnlyCollection<TaskInfo>> GetActiveTasksByTypeAsync(short taskTypeId, CancellationToken cancellationToken)
        {
            return Task.FromResult((IReadOnlyCollection<TaskInfo>)TaskInfos.Where(t => t.Status != TaskManagement.TaskStatus.Completed));
        }

        public Task<TaskInfo> GetTaskAsync(string taskId, CancellationToken cancellationToken)
        {
            TaskInfo taskInfo = TaskInfos.FirstOrDefault(t => taskId.Equals(t.TaskId));

            return Task.FromResult(GetTaskInfoFunc?.Invoke(taskInfo));
        }
    }
}
