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

        public string ProcessingTaskQueueId { get; set; }

        /// <summary>
        /// Controls how many resources will be returned for each search query while importing the data.
        /// </summary>
        public int MaximumConcurrency { get; set; } = DefaultMaximumConcurrency;

        public int MaximumConcurrentRebuildOperationCount { get; set; } = DefaultMaximumConcurrentRebuildOperationCount;
    }
}
