// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.JobManagement
{
    public class JobInfo
    {
        public long Id { get; set; }

        public byte QueueType { get; set; }

        public JobStatus? Status { get; set; }

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
    }
}
