﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class ImportTaskConfiguration
    {
        private const int DefaultMaxRunningProcessingTaskCount = 5;
        private const int DefaultMaxRetryCount = 5;
        private const int DefaultSqlImportBatchSizeForCheckpoint = 80000;
        private const int DefaultSqlBatchSizeForImportResourceOperation = 2000;
        private const int DefaultSqlBatchSizeForImportParamsOperation = 10000;
        private const int DefaultSqlMaxImportOperationConcurrentCount = 5;
        private const int DefaultSqlCleanResourceBatchSize = 1000;
        private const int DefaultSqlMaxRebuildIndexOperationConcurrentCount = 3;
        private const int DefaultSqlMaxDeleteDuplicateOperationConcurrentCount = 3;
        private const int DefaultSqlLongRunningOperationTimeoutInSec = 60 * 60 * 2;
        private const int DefaultSqlBulkOperationTimeoutInSec = 60 * 10;

        /// <summary>
        /// Determines whether bulk import is enabled or not.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Initial import mode
        /// </summary>
        public bool InitialImportMode { get; set; }

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
        public int SqlLongRunningOperationTimeoutInSec { get; set; } = DefaultSqlLongRunningOperationTimeoutInSec;

        /// <summary>
        /// SQL bulk operation timeout in seconds
        /// </summary>
        public int SqlBulkOperationTimeoutInSec { get; set; } = DefaultSqlBulkOperationTimeoutInSec;

        /// <summary>
        /// Max batch size for import resource operation
        /// </summary>
        public int SqlBatchSizeForImportResourceOperation { get; set; } = DefaultSqlBatchSizeForImportResourceOperation;

        /// <summary>
        /// Max batch size for import resoruce search params operation
        /// </summary>
        public int SqlBatchSizeForImportParamsOperation { get; set; } = DefaultSqlBatchSizeForImportParamsOperation;

        /// <summary>
        /// Max concurrent count for import operation
        /// </summary>
        public int SqlMaxImportOperationConcurrentCount { get; set; } = DefaultSqlMaxImportOperationConcurrentCount;

        /// <summary>
        /// Checkpoint batch size
        /// </summary>
        public int SqlImportBatchSizeForCheckpoint { get; set; } = DefaultSqlImportBatchSizeForCheckpoint;

        /// <summary>
        /// Batch size to clean duplicated resource with same resource id.
        /// </summary>
        public int SqlCleanResourceBatchSize { get; set; } = DefaultSqlCleanResourceBatchSize;

        /// <summary>
        /// Concurrent count for rebuild index operation.
        /// </summary>
        public int SqlMaxRebuildIndexOperationConcurrentCount { get; set; } = DefaultSqlMaxRebuildIndexOperationConcurrentCount;

        /// <summary>
        /// Concurrent count for delete duplicate resource operation.
        /// </summary>
        public int SqlMaxDeleteDuplicateOperationConcurrentCount { get; set; } = DefaultSqlMaxDeleteDuplicateOperationConcurrentCount;

        /// <summary>
        /// Disable optional index during import data.
        /// </summary>
        public bool DisableOptionalIndexesForImport { get; set; }

        /// <summary>
        /// Disable unique optional index during import data.
        /// </summary>
        public bool DisableUniqueOptionalIndexesForImport { get; set; }
    }
}
