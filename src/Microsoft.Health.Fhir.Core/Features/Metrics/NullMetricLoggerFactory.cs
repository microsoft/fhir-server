// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Metrics
{
    /// <summary>
    /// An implementation of IMetricLoggerFactory that returns NullMetricLogger instances.
    /// </summary>
    public class NullMetricLoggerFactory : IMetricLoggerFactory
    {
        public IMetricLogger CreateMetricLogger(string metricName, params string[] dimensions)
        {
            return new NullMetricLogger();
        }
    }
}
