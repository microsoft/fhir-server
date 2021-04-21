// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.TaskManagement
{
    public interface ITaskManager
    {
        public Task<TaskInfo> CreateTaskAsync(TaskInfo task, CancellationToken cancellationToken);

        public Task<TaskInfo> GetTaskAsync(string taskId, CancellationToken cancellationToken);

        public Task<TaskInfo> CancelTaskAsync(string taskId, CancellationToken cancellationToken);
    }
}
