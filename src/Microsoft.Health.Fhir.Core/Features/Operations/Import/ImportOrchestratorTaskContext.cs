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
    }
}
