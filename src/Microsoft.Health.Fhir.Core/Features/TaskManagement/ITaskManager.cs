// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.TaskManagement
{
    public interface ITaskManager
    {
        public Task<TaskInfo> CreateTaskAsync(TaskInfo task);

        public Task<TaskInfo> GetTaskAsync(string taskId);

        public Task<TaskInfo> CancelTaskAsync(TaskInfo task);
    }
}
