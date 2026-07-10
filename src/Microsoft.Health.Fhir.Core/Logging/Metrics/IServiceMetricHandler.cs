// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Logging.Metrics
{
    public interface IServiceMetricHandler
    {
        /// <summary>
        /// Reports the availability of the FHIR service. Emission is throttled so the metric is
        /// published at most once per minute regardless of how often it is invoked.
        /// </summary>
        /// <param name="isAvailable"><c>true</c> when the service is considered available; otherwise <c>false</c>.</param>
        void EmitAvailability(bool isAvailable);
    }
}
