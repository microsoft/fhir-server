// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Api.Features.Health
{
    /// <summary>
    /// Immutable snapshot of the improper-behavior health state so the health flag and its
    /// accompanying message are always published together atomically.
    /// </summary>
    internal sealed record ImproperBehaviorHealthCheckState(bool IsHealthy, string Message)
    {
        public static readonly ImproperBehaviorHealthCheckState Healthy = new(true, string.Empty);
    }
}
