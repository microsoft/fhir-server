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
        bool IsInitialized();

        public Task<IEnumerable<TaskInfo>> EnqueueAsync(byte queueType, string[] definitions, long? groupId, bool forceOneActiveJobGroup, bool isCompleted, CancellationToken cancellationToken);

        public Task<TaskInfo> DequeueAsync(byte queueType, byte startPartitionId, string worker, int heartbeatTimeoutSec, CancellationToken cancellationToken);

        public Task<TaskInfo> GetTaskByIdAsync(byte queueType, long taskId, bool returnDefinition, CancellationToken cancellationToken);

        public Task<IEnumerable<TaskInfo>> GetTaskByIdsAsync(byte queueType, long[] taskIds, bool returnDefinition, CancellationToken cancellationToken);

        public Task<IEnumerable<TaskInfo>> GetTaskByGroupIdAsync(byte queueType, long groupId, bool returnDefinition, CancellationToken cancellationToken);

        public Task<TaskInfo> KeepAliveTaskAsync(TaskInfo taskInfo, CancellationToken cancellationToken);

        public Task CancelTaskAsync(byte queueType, long? groupId, long? jobId, CancellationToken cancellationToken);

        public Task CompleteTaskAsync(TaskInfo taskInfo, bool requestCancellationOnFailure, CancellationToken cancellationToken);
    }
}
