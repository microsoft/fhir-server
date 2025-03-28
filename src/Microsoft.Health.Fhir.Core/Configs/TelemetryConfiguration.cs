// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Telemetry;

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class TelemetryConfiguration
    {
        public string ConnectionString { get; set; }

        public string InstrumentationKey { get; set; }

        public TelemetryProvider Provider { get; set; } = TelemetryProvider.None;
    }
}
