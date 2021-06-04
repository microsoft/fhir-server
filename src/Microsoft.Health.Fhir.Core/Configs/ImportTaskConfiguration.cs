// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class ImportTaskConfiguration
    {
        private const int DefaultMaxRunningProcessingTaskCount = 5;
        private const int DefaultMaxRetryCount = 5;
        private const int DefaultBatchSizeForCheckpoint = 5000;
        private const int DefaultMaxBatchSizeForImportOperation = 1000;
        private const int DefaultMaxImportOperationConcurrentCount = 3;
        private const int DefaultLongRunningOperationTimeoutInSec = 60 * 60 * 2;

        /// <summary>
        /// Determines whether bulk import is enabled or not.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Queue id for data processing task. it might be different from orchestraotr task for standalone runtime environment.
        /// </summary>
        public string ProcessingTaskQueueId { get; set; }

        /// <summary>
        /// Controls how many data processing task would run at the same time.
        /// </summary>
        public int MaxRunningProcessingTaskCount { get; set; } = DefaultMaxRunningProcessingTaskCount;

        /// <summary>
        /// Controls how many data processing task would run at the same time.
        /// </summary>
        public short MaxRetryCount { get; set; } = DefaultMaxRetryCount;

        /// <summary>
        /// Long running operation timeout
        /// </summary>
        public int LongRunningOperationTimeoutInSec { get; set; } = DefaultLongRunningOperationTimeoutInSec;

        /// <summary>
        /// Max batch size for import operation
        /// </summary>
        public int MaxBatchSizeForImportOperation { get; set; } = DefaultMaxBatchSizeForImportOperation;

        /// <summary>
        /// Max concurrent count for import operation
        /// </summary>
        public int MaxImportOperationConcurrentCount { get; set; } = DefaultMaxImportOperationConcurrentCount;

        /// <summary>
        /// Checkpoint batch size
        /// </summary>
        public int BatchSizeForCheckpoint { get; set; } = DefaultBatchSizeForCheckpoint;
    }
}
