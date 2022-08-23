// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportOrchestratorJobResult
    {
        /// <summary>
        /// Transaction time for import job created
        /// </summary>
        public DateTimeOffset TransactionTime { get; set; }

        /// <summary>
        /// Request Uri for the import opearion
        /// </summary>
        public string Request { get; set; }

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
        /// Created job count for all blob files
        /// </summary>
        public int CreatedJobCount { get; set; }

        /// <summary>
        /// Current end sequence id
        /// </summary>
        public long CurrentSequenceId { get; set; }

        /// <summary>
        /// Current running job id list
        /// </summary>
        public ISet<long> RunningJobIds { get; } = new HashSet<long>();

        /// <summary>
        /// Orchestrator job progress.
        /// </summary>
        public ImportOrchestratorJobProgress Progress { get; set; }

        /// <summary>
        /// Rebuild index progress.
        /// </summary>
        public RebuildIndexProgress IndexProgress { get; set; }

        /// <summary>
        /// Record the biggest partition id for each index.
        /// </summary>
        public Dictionary<string, int> AlreadyCompletePartitionIds { get; } = new Dictionary<string, int>();
    }
}
