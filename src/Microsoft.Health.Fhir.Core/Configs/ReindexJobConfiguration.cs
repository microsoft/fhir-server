// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Operations;

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class ReindexJobConfiguration : HostingBackgroundServiceQueueItem
    {
        public ReindexJobConfiguration()
        {
            Queue = QueueType.Reindex;
        }

        /// <summary>
        /// Controls how many resources will be processed by a processing job
        /// </summary>
        public int MaximumNumberOfResourcesPerQuery { get; set; } = 10000;

        /// <summary>
        /// Controls how many resources will be batched within a processing
        /// </summary>
        public int MaximumNumberOfResourcesPerWrite { get; set; } = 1000;

        /// <summary>
        /// Controls the multiplier applied to the SearchParameterCacheRefreshIntervalSeconds
        /// to determine time to wait for search param cache refresh. Relevant for Cosmos only.
        /// </summary>
        public int CacheRefreshWaitMultiplier { get; set; } = 3;

        /// <summary>
        /// Controls the multiplier applied to the SearchParameterCacheRefreshIntervalSeconds
        /// to determine max time to wait for search param cache refresh. Relevant for SQL only.
        /// </summary>
        public int CacheUpdateMaxWaitMultiplier { get; set; } = 40;

        /// <summary>
        /// Controls the multiplier applied to the SearchParameterCacheRefreshIntervalSeconds
        /// to determine the time interval to retrieve active host names. Relevant for SQL only.
        /// </summary>
        public int ActiveHostsEventsMultiplier { get; set; } = 9;

        /// <summary>
        /// Controls orchestrator job info polling interval
        /// </summary>
        public int JobsPollingIntervalSec { get; set; } = 30;

        /// <summary>
        /// Controls number of jobs orchestrator pulls from the database in a single call
        /// </summary>
        public int JobsBatchSize { get; set; } = 1000;

        /// <summary>
        /// Controls how many surrogate ID ranges are fetched per database call when calculating
        /// job ranges. Uses batched calls to avoid timeout on large tables.
        /// </summary>
        public int NumberOfRecordRanges { get; set; } = 100;
    }
}
