// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.TaskManagement
{
    public interface IQueueClient
    {
        /// <summary>
        /// Ensure the client initialized.
        /// </summary>
        bool IsInitialized();

        /// <summary>
        /// Enqueue new tasks
        /// </summary>
        /// <param name="queueType">Queue Type for new tasks</param>
        /// <param name="definitions">Task definiation</param>
        /// <param name="groupId">Group id for tasks. Optional</param>
        /// <param name="forceOneActiveJobGroup">Only enqueue task only if there's no active task with same queue type.</param>
        /// <param name="isCompleted">Enqueue completed tasks.</param>
        /// <param name="cancellationToken">Cancellation Token</param>
        /// <returns>Task ids for all tasks, include existed tasks.</returns>
        public Task<IEnumerable<TaskInfo>> EnqueueAsync(byte queueType, string[] definitions, long? groupId, bool forceOneActiveJobGroup, bool isCompleted, CancellationToken cancellationToken);

        /// <summary>
        /// Dequeue task
        /// </summary>
        /// <param name="queueType">Queue Type</param>
        /// <param name="startPartitionId">Start dequeue partition id</param>
        /// <param name="worker">current worker name</param>
        /// <param name="heartbeatTimeoutSec">Heartbeat timeout for retry</param>
        /// <param name="cancellationToken">Cancellation Token</param>
        public Task<TaskInfo> DequeueAsync(byte queueType, byte startPartitionId, string worker, int heartbeatTimeoutSec, CancellationToken cancellationToken);

        /// <summary>
        /// Get task by id
        /// </summary>
        /// <param name="queueType">Queue Type</param>
        /// <param name="taskId">Task id</param>
        /// <param name="returnDefinition">Return definition</param>
        /// <param name="cancellationToken">Cancellation token</param>
        public Task<TaskInfo> GetTaskByIdAsync(byte queueType, long taskId, bool returnDefinition, CancellationToken cancellationToken);

        /// <summary>
        /// Get task by id
        /// </summary>
        /// <param name="queueType">Queue Type</param>
        /// <param name="taskIds">Task ids list</param>
        /// <param name="returnDefinition">Return definition</param>
        /// <param name="cancellationToken">Cancellation token</param>
        public Task<IEnumerable<TaskInfo>> GetTaskByIdsAsync(byte queueType, long[] taskIds, bool returnDefinition, CancellationToken cancellationToken);

        /// <summary>
        /// Get task by group id
        /// </summary>
        /// <param name="queueType">Queue Type</param>
        /// <param name="groupId">Task group id</param>
        /// <param name="returnDefinition">Return definition</param>
        /// <param name="cancellationToken">Cancellation token</param>
        public Task<IEnumerable<TaskInfo>> GetTaskByGroupIdAsync(byte queueType, long groupId, bool returnDefinition, CancellationToken cancellationToken);

        /// <summary>
        /// Send heart beat to keep alive task
        /// </summary>
        /// <param name="taskInfo">Task Info to keep alive</param>
        /// <param name="cancellationToken">Cancellation Token</param>
        public Task<TaskInfo> KeepAliveTaskAsync(TaskInfo taskInfo, CancellationToken cancellationToken);

        /// <summary>
        /// Cancel tasks by group id or job id
        /// </summary>
        /// <param name="queueType">Queue Type</param>
        /// <param name="groupId">Task group id</param>
        /// <param name="taskId">Task id</param>
        /// <param name="cancellationToken">Cancellation Token</param>
        public Task CancelTaskAsync(byte queueType, long? groupId, long? taskId, CancellationToken cancellationToken);

        /// <summary>
        /// Complete task
        /// </summary>
        /// <param name="taskInfo">Task info for complete</param>
        /// <param name="requestCancellationOnFailure">Cancel other tasks with same group id if this task failed.</param>
        /// <param name="cancellationToken">Cancellation token</param
        public Task CompleteTaskAsync(TaskInfo taskInfo, bool requestCancellationOnFailure, CancellationToken cancellationToken);
    }
}
