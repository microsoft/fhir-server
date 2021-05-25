// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class TaskHostingConfiguration
    {
        public bool Enabled { get; set; }

        public string QueueId { get; set; }

        public int TaskHeartbeatTimeoutThresholdInSeconds { get; set; }
    }
}
