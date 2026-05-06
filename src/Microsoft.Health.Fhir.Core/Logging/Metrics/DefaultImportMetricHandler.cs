// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.Metrics;

namespace Microsoft.Health.Fhir.Core.Logging.Metrics
{
    public sealed class DefaultImportMetricHandler : BaseMeterMetricHandler, IImportMetricHandler
    {
        private readonly Counter<int> _importFailureCounter;
        private readonly Counter<int> _importSuccessCounter;

        public DefaultImportMetricHandler(IMeterFactory meterFactory)
            : base(meterFactory)
        {
            _importFailureCounter = MetricMeter.CreateCounter<int>("Import.Failure");
            _importSuccessCounter = MetricMeter.CreateCounter<int>("Import.Success");
        }

        public void EmitFailure()
        {
            _importFailureCounter.Add(1);
        }

        public void EmitSuccess()
        {
            _importSuccessCounter.Add(1);
        }
    }
}
