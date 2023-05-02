// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class ImportJobConfiguration
    {
        private const int DefaultTransactionSize = 1000;
        private const int DefaultSqlIndexRebuildThreads = 3;
        private const int DefaultInfinitySqlTimeoutSec = 0;
        private const int DefaultPollingPeriodSec = 60;

        /// <summary>
        /// Determines whether bulk import is enabled or not.
        /// </summary>
        public bool Enabled { get; set; }

        public int InfinitySqlTimeoutSec { get; set; } = DefaultInfinitySqlTimeoutSec;

        /// <summary>
        /// Max batch size for import resource operation
        /// </summary>
        public int TransactionSize { get; set; } = DefaultTransactionSize;

        /// <summary>
        /// Concurrent count for rebuild index operation.
        /// </summary>
        public int SqlIndexRebuildThreads { get; set; } = DefaultSqlIndexRebuildThreads;

        /// <summary>
        /// How often polling for new import jobs happens.
        /// </summary>
        public int PollingPeriodSec { get; set; } = DefaultPollingPeriodSec;

        /// <summary>
        /// Disable optional index during import data.
        /// </summary>
        public bool DisableOptionalIndexesForImport { get; set; }
    }
}
