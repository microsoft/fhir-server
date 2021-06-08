// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.TaskManagement
{
    public static class Constants
    {
        public const int DefaultPollingFrequencyInSeconds = 10;

        public const short DefaultMaxRunningTaskCount = 1;

        public const short DefaultMaxRetryCount = 3;

        public const int DefaultTaskHeartbeatTimeoutThresholdInSeconds = 600;

        public const int DefaultTaskHeartbeatIntervalInSeconds = 10;
    }
}
