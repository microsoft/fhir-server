// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics.Metrics;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Logging.Metrics
{
    public abstract class BaseSuccessRateMetricHandler : BaseMeterMetricHandler, ISuccessRateMetricHandler
    {
        private readonly Counter<int> _failureCounter;
        private readonly Counter<int> _successCounter;

        protected BaseSuccessRateMetricHandler(IMeterFactory meterFactory, string successMetricName, string failureMetricName)
            : base(meterFactory)
        {
            EnsureArg.IsNotNullOrWhiteSpace(successMetricName, nameof(successMetricName));
            EnsureArg.IsNotNullOrWhiteSpace(failureMetricName, nameof(failureMetricName));

            _failureCounter = MetricMeter.CreateCounter<int>(failureMetricName);
            _successCounter = MetricMeter.CreateCounter<int>(successMetricName);
        }

        public void EmitFailure(string errorType)
        {
            EnsureArg.IsNotNullOrWhiteSpace(errorType, nameof(errorType));

            _failureCounter.Add(1, new KeyValuePair<string, object>("ExceptionType", errorType));
        }

        public void EmitSuccess()
        {
            _successCounter.Add(1);
        }
    }
}
