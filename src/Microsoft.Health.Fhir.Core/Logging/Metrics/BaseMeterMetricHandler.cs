// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.Metrics;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Logging.Metrics
{
    public abstract class BaseMeterMetricHandler
    {
        public const string MeterName = "FhirServer";

        protected BaseMeterMetricHandler(IMeterFactory meterFactory)
        {
            EnsureArg.IsNotNull(meterFactory, nameof(meterFactory));

            MetricMeter = meterFactory.Create(MeterName);
        }

        protected Meter MetricMeter { get; }
    }
}
