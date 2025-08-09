// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Operations;

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class ImportJobConfiguration : HostingBackgroundServiceQueueItem
    {
        private const int DefaultTransactionSize = 1000;
        private const int DefaultInfinitySqlTimeoutSec = 0;
        private const int DefaultPollingPeriodSec = 60;

        public ImportJobConfiguration()
        {
            Queue = QueueType.Import;
        }

        /// <summary>
        /// Initial import mode
        /// </summary>
        public bool InitialImportMode { get; set; }

        public int InfinitySqlTimeoutSec { get; set; } = DefaultInfinitySqlTimeoutSec;

        /// <summary>
        /// Max batch size for import resource operation
        /// </summary>
        public int TransactionSize { get; set; } = DefaultTransactionSize;

        /// <summary>
        /// Concurrent count for rebuild index operation.
        /// When set to 0 or negative, will use adaptive threading based on system resources.
        /// Defaults to adaptive threading (0).
        /// </summary>
        public int SqlIndexRebuildThreads { get; set; } = 0;

        /// <summary>
        /// Maximum number of concurrent import operations to prevent resource exhaustion.
        /// When set to 0 or negative, will use adaptive threading based on system resources.
        /// </summary>
        public int MaxConcurrentImportOperations { get; set; } = 0;

        /// <summary>
        /// How often polling for new import jobs happens.
        /// </summary>
        public int PollingFrequencyInSeconds { get; set; } = DefaultPollingPeriodSec; // FYI By definition, frequency cannot be measured in time units.

        /// <summary>
        /// Disable optional index during import data.
        /// </summary>
        public bool DisableOptionalIndexesForImport { get; set; }
    }
}
