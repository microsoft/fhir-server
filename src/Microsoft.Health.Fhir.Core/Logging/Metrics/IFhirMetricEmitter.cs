// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Logging.Metrics
{
    public interface IFhirMetricEmitter
    {
        void EmitBundleLatency(ILatencyMetricNotification latencyMetricNotification);

        void EmitSearchLatency(ILatencyMetricNotification latencyMetricNotification);

        void EmitCrudLatency(ILatencyMetricNotification latencyMetricNotification);
    }
}
