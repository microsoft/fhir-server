// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.TaskManagement
{
    public interface ITaskConsumer
    {
        Task<TaskInfo> CompleteAsync(string taskId, TaskResultData result, string runId, CancellationToken cancellationToken);

        Task<IReadOnlyCollection<TaskInfo>> GetNextMessagesAsync(short count, int taskHeartbeatTimeoutThresholdInSeconds, CancellationToken cancellationToken);

        Task<TaskInfo> KeepAliveAsync(string taskId, string runId, CancellationToken cancellationToken);

        Task<TaskInfo> ResetAsync(string taskId, TaskResultData result, string runId, short maxRetryCount, CancellationToken cancellationToken);
    }
}
