// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class ReindexJobConfiguration
    {
        /// <summary>
        /// Determines whether reindexing feature is enabled or not.
        /// </summary>
        public bool Enabled { get; set; } = false;

        public ushort ConsecutiveFailuresThreshold { get; set; } = 5;

        /// <summary>
        /// Controls default number of threads applied to a reindex job if parameter is not specified in the POST
        /// </summary>
        public ushort DefaultMaximumThreadsPerReindexJob { get; set; } = 1;

        /// <summary>
        /// Controls time limit before a job is considered stale and needs to be recovered
        /// </summary>
        public TimeSpan JobHeartbeatTimeoutThreshold { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Controls time between queries checking for reindex jobs
        /// </summary>
        public TimeSpan JobPollingFrequency { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Controls the time between queries of resources to be reindexed
        /// </summary>
        public TimeSpan QueryDelayIntervalInMilliseconds { get; set; } = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// Controls how many resources will be returned in a batch for reindexing
        /// </summary>
        public uint MaximumNumberOfResourcesPerQuery { get; set; } = 100;

        /// <summary>
        /// Controls how many reindex jobs are allowed to be running at one time
        /// currently fixed at 1
        /// </summary>
        public ushort MaximumNumberOfConcurrentJobsAllowed { get; internal set; } = 1;
    }
}
