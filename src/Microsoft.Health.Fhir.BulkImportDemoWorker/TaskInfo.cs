// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.BulkImportDemoWorker
{
    public class TaskInfo
    {
        public string TaskId { get; set; }

        public string GroupId { get; set; }

        public string QueueId { get; set; }

        public int Status { get; set; }

        public int TaskTypeId { get; set; }

        public bool IsCanceled { get; set; }

        public DateTime HeartbeatDateTime { get; set; }

        public string InputData { get; set; }

        public string TaskContext { get; set; }
    }
}
