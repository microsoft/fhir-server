// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Metrics
{
    /// <summary>
    /// An implementation of IMetricLogger that does nothing.
    /// </summary>
    public class NullMetricLogger : IMetricLogger
    {
        public void LogMetric(long inputValue, params string[] dimensions)
        {
        }
    }
}
