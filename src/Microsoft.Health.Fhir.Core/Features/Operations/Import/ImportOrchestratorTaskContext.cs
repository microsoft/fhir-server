// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.TaskManagement;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportOrchestratorTaskContext
    {
        /// <summary>
        /// Data processing task records.
        /// </summary>
#pragma warning disable CA2227 // Need to update status during execution.
        public IDictionary<Uri, TaskInfo> DataProcessingTasks { get; set; } = new Dictionary<Uri, TaskInfo>();
#pragma warning restore CA2227

        /// <summary>
        /// Orchestrator task progress.
        /// </summary>
        public ImportOrchestratorTaskProgress Progress { get; set; }

        /// <summary>
        /// Import result during execution
        /// </summary>
        public ImportTaskResult ImportResult { get; set; }

        /// <summary>
        /// Import total file size
        /// </summary>
        public long? TotalSizeInBytes { get; set; }

        /// <summary>
        /// Resource count succeed to import
        /// </summary>
        public long SucceedImportCount { get; set; }

        /// <summary>
        /// Resource count failed to import
        /// </summary>
        public long FailedImportCount { get; set; }

        /// <summary>
        /// Created task count for all blob files
        /// </summary>
        public int CreatedTaskCount { get; set; }

        /// <summary>
        /// Current end sequence id
        /// </summary>
        public long CurrentSequenceId { get; set; }

        /// <summary>
        /// Current running task id list
        /// </summary>
#pragma warning disable CA1002 // Do not expose generic lists
        public List<string> RunningTaskIds { get; } = new List<string>();
#pragma warning restore CA1002 // Do not expose generic lists
    }
}
