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
        /// Controls the time between queries of resources to be reindexed
        /// </summary>
        public int QueryDelayIntervalInMilliseconds { get; set; } = 100;

        /// <summary>
        /// Controls how many resources will be returned in a batch for reindexing
        /// </summary>
        public uint MaximumNumberOfResourcesPerQuery { get; set; } = 10000;

        /// <summary>
        /// Controls how many resources will be batched to reindex within a job (e.g. Job of 10k will batch 1k at a time to database to reindex)
        /// </summary>
        public uint MaximumNumberOfResourcesPerWrite { get; set; } = 1000;

        /// <summary>
        /// Controls how many reindex jobs are allowed to be running at one time
        /// currently fixed at 1
        /// </summary>
        public ushort MaximumNumberOfConcurrentJobsAllowed { get; internal set; } = 1;

        /// <summary>
        /// Controls the target percentage of how much of the allocated
        /// data store resources to use
        /// </summary>
        public ushort? TargetDataStoreResourcePercentage { get; set; } = null;

        /// <summary>
        /// Controls the multiplier applied to the SearchParameterCacheRefreshIntervalSeconds
        /// to determine how long to wait before starting the reindex job processing
        /// </summary>
        public int ReindexDelayMultiplier { get; set; } = 3;
    }
}
