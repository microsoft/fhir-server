// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Logging.Metrics;

namespace Microsoft.Health.Fhir.Core.Logging.Metrics
{
    /// <summary>
    /// <see cref="DefaultMetricEmitter"/> does not emit any metrics.
    /// It's a default implementation to support the case where no metrics are required.
    /// </summary>
    public sealed class DefaultMetricEmitter : IFhirMetricEmitter
    {
        public void EmitBundleLatency(ILatencyMetricNotification latencyMetricNotification)
        {
        }

        public void EmitCrudLatency(ILatencyMetricNotification latencyMetricNotification)
        {
        }

        public void EmitSearchLatency(ILatencyMetricNotification latencyMetricNotification)
        {
        }
    }
}
