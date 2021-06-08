// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.TaskManagement
{
    /// <summary>
    /// Interface to manage the task: create/cancel/get task.
    /// </summary>
    public interface ITaskManager
    {
        /// <summary>
        /// Create task for task information
        /// </summary>
        /// <param name="task">Task information.</param>
        /// <param name="cancellationToken">Cancellation Token</param>
        /// <returns>Task information after created.</returns>
        public Task<TaskInfo> CreateTaskAsync(TaskInfo task, CancellationToken cancellationToken);

        /// <summary>
        /// Get task information by id.
        /// </summary>
        /// <param name="taskId">Id for the task</param>
        /// <param name="cancellationToken">Cancellation Token</param>
        /// <returns>Current task information.</returns>
        public Task<TaskInfo> GetTaskAsync(string taskId, CancellationToken cancellationToken);

        /// <summary>
        /// Cancel task by id.
        /// </summary>
        /// <param name="taskId">Id for the task</param>
        /// <param name="cancellationToken">Cancellation Token</param>
        /// <returns>Task information after cancel.</returns>
        public Task<TaskInfo> CancelTaskAsync(string taskId, CancellationToken cancellationToken);
    }
}
