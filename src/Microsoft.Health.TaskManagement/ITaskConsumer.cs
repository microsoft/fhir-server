// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.TaskManagement
{
    /// <summary>
    /// Interface to consume the task: pull/keepalive/complete/reset.
    /// </summary>
    public interface ITaskConsumer
    {
        /// <summary>
        /// Complete the task with result.
        /// </summary>
        /// <param name="taskId">Id for the task</param>
        /// <param name="taskResultData">Result data for the task execution</param>
        /// <param name="runId">Run id for this task execution</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task infomation after status updated</returns>
        Task<TaskInfo> CompleteAsync(string taskId, TaskResultData taskResultData, string runId, CancellationToken cancellationToken);

        /// <summary>
        /// Get next available tasks in task queue.
        /// </summary>
        /// <param name="count">max retrieve task batch count</param>
        /// <param name="taskHeartbeatTimeoutThresholdInSeconds">heartbeat timeout threshold in seconds</param>
        /// <param name="cancellationToken">Cancelllation Token</param>
        /// <returns>List of available tasks</returns>
        Task<IReadOnlyCollection<TaskInfo>> GetNextMessagesAsync(short count, int taskHeartbeatTimeoutThresholdInSeconds, CancellationToken cancellationToken);

        /// <summary>
        /// Send keep alive request for task heartbeat
        /// </summary>
        /// <param name="taskId">Id for the task</param>
        /// <param name="runId">Run id for this task execution</param>
        /// <param name="cancellationToken">Cancelllation Token</param>
        /// <returns>Task infomation after status updated</returns>
        Task<TaskInfo> KeepAliveAsync(string taskId, string runId, CancellationToken cancellationToken);

        /// <summary>
        /// Reset task status and allow repickup by others
        /// </summary>
        /// <param name="taskId">Id for the task</param>
        /// <param name="taskResultData">Result data for the task execution</param>
        /// <param name="runId">Run id for this task execution</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task infomation after status updated</returns>
        Task<TaskInfo> ResetAsync(string taskId, TaskResultData taskResultData, string runId, CancellationToken cancellationToken);
    }
}
