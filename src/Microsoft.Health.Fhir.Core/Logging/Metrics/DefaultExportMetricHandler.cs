// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.Metrics;

namespace Microsoft.Health.Fhir.Core.Logging.Metrics
{
    public sealed class DefaultExportMetricHandler : BaseMeterMetricHandler, IExportMetricHandler
    {
        private readonly Counter<int> _exportFailureCounter;
        private readonly Counter<int> _exportSuccessCounter;

        public DefaultExportMetricHandler(IMeterFactory meterFactory)
            : base(meterFactory)
        {
            _exportFailureCounter = MetricMeter.CreateCounter<int>("Export.Failure");
            _exportSuccessCounter = MetricMeter.CreateCounter<int>("Export.Success");
        }

        public void EmitFailure()
        {
            _exportFailureCounter.Add(1);
        }

        public void EmitSuccess()
        {
            _exportSuccessCounter.Add(1);
        }
    }
}
