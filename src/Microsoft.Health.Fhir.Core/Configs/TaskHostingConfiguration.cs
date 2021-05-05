// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class TaskHostingConfiguration
    {
        public const string DefaultQueueId = "default";

        public bool Enabled { get; set; }

        public string QueueId { get; set; } = DefaultQueueId;

        public int? TaskHeartbeatTimeoutThresholdInSeconds { get; set; }

        public int? PollingFrequencyInSeconds { get; set; }

        public short? MaxRunningTaskCount { get; set; }

        public int? TaskHeartbeatIntervalInSeconds { get; set; }
    }
}
