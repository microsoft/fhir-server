// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Logging.Metrics
{
    public sealed class SearchMetricHandler
    {
        private readonly IFhirMetricEmitter _metricEmitter;

        public SearchMetricHandler(IFhirMetricEmitter metricEmitter)
        {
            EnsureArg.IsNotNull(metricEmitter, nameof(metricEmitter));

            _metricEmitter = metricEmitter;
        }

        public void EmitSearchLatency(SearchMetricNotification searchMetricNotification)
        {
            _metricEmitter.EmitSearchLatency(searchMetricNotification);
        }
    }
}
