// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class RedisConnectionConfiguration
    {
        public bool AbortOnConnectFail { get; set; } = false;

        public int ConnectRetry { get; set; } = 3;

        public int ConnectTimeout { get; set; } = 5000;

        public int SyncTimeout { get; set; } = 5000;

        public int AsyncTimeout { get; set; } = 5000;
    }
}
