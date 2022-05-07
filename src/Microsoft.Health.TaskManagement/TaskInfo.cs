// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.TaskManagement
{
    public class TaskInfo
    {
        public long Id { get; set; }

        public QueueType QueueType { get; set; }

        public TaskStatus? Status { get; set; }

        public long GroupId { get; set; }

        public string Definition { get; set; }

        public string Result { get; set; }

        public long? Data { get; set; }

        public bool CancelRequested { get; set; }

        public long Version { get; set; }

        public long Priority { get; set; }

        public DateTime CreateDate { get; set; }

        public DateTime? StartDate { get; set; }

        public DateTime? EndDate { get; set; }

        public DateTime HeartbeatDateTime { get; set; }

        //---------------------------

        public string TaskId { get; set; }

        public string QueueId { get; set; }

        public short TaskTypeId { get; set; }

        public string RunId { get; set; }

        public short RetryCount { get; set; }

        public short? MaxRetryCount { get; set; }

        public string Context { get; set; }

        public string ParentTaskId { get; set; }
    }
}
