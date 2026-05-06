// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.Metrics;

namespace Microsoft.Health.Fhir.Core.Logging.Metrics
{
    public sealed class DefaultReindexMetricHandler : BaseMeterMetricHandler, IReindexMetricHandler
    {
        private readonly Counter<int> _reindexFailureCounter;
        private readonly Counter<int> _reindexSuccessCounter;

        public DefaultReindexMetricHandler(IMeterFactory meterFactory)
            : base(meterFactory)
        {
            _reindexFailureCounter = MetricMeter.CreateCounter<int>("Reindex.Failure");
            _reindexSuccessCounter = MetricMeter.CreateCounter<int>("Reindex.Success");
        }

        public void EmitFailure()
        {
            _reindexFailureCounter.Add(1);
        }

        public void EmitSuccess()
        {
            _reindexSuccessCounter.Add(1);
        }
    }
}
