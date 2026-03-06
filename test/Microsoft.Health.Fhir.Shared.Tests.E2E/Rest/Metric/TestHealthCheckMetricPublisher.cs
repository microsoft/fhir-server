// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Health.Fhir.Core.Logging.Metrics;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Metric
{
    public class TestHealthCheckMetricPublisher : IHealthCheckMetricPublisher
    {
        public bool WasPublished { get; private set; }

        public HealthStatus? LastPublishedStatus { get; private set; }

        public void Reset()
        {
            WasPublished = false;
            LastPublishedStatus = null;
        }

        public void Publish(HealthReport healthReport)
        {
            WasPublished = true;
            LastPublishedStatus = healthReport.Status;
        }
    }
}
