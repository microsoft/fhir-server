// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.TaskManagement
{
    public class TaskInfo
    {
        public string TaskId { get; set; }

        public string QueueId { get; set; }

        public TaskStatus? Status { get; set; }

        public short TaskTypeId { get; set; }

        public string RunId { get; set; }

        public bool IsCanceled { get; set; }

        public short RetryCount { get; set; }

        public short? MaxRetryCount { get; set; }

        public DateTime? HeartbeatDateTime { get; set; }

        public string InputData { get; set; }

        public string Context { get; set; }

        public string Result { get; set; }
    }
}
