﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Features.Telemetry
{
    [Flags]
    public enum TelemetryProvider
    {
        None = 0,
        ApplicationInsights = 1,
        OpenTelemetry = 2,
    }
}
