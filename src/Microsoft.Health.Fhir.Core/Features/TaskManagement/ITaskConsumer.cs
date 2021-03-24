// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.TaskManagement
{
    public interface ITaskConsumer
    {
        Task<TaskInfo> CompleteAsync(string taskId, TaskResultData result, string runId);

        Task<IReadOnlyCollection<TaskInfo>> GetNextMessagesAsync(int count, int taskHeartbeatTimeoutThresholdInSeconds);

        Task<TaskInfo> KeepAliveAsync(string taskId, string runId);

        Task ResetAsync(string taskId, TaskResultData result, string runId);
    }
}
