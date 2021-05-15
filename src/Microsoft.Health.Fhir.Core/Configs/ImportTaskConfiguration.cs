// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class ImportTaskConfiguration
    {
        private const int DefaultMaximumConcurrency = 5;
        private const int DefaultMaximumConcurrentRebuildOperationCount = 3;

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
        public int MaximumConcurrency { get; set; } = DefaultMaximumConcurrency;

        /// <summary>
        /// For bulk import operation, we need to rebuild index after data imported. This setting is to control the rebuild index operation concurrent.
        /// </summary>
        public int MaximumConcurrentRebuildOperationCount { get; set; } = DefaultMaximumConcurrentRebuildOperationCount;
    }
}
