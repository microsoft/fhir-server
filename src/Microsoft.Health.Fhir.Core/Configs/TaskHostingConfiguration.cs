// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class TaskHostingConfiguration
    {
        public const string DefaultQueueId = "default";

        /// <summary>
        /// Enable the task hosting service.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// The queue id for task hosting to pull task.
        /// </summary>
        public string QueueId { get; set; } = DefaultQueueId;

        /// <summary>
        /// Heartbeat timeout for task.
        /// </summary>
        public int? TaskHeartbeatTimeoutThresholdInSeconds { get; set; }

        /// <summary>
        /// Polling frequency for task hosting to pull task.
        /// </summary>
        public int? PollingFrequencyInSeconds { get; set; }

        /// <summary>
        /// Max running task count at the same time.
        /// </summary>
        public short? MaxRunningTaskCount { get; set; }

        /// <summary>
        /// Heartbeat request interval.
        /// </summary>
        public int? TaskHeartbeatIntervalInSeconds { get; set; }
    }
}
