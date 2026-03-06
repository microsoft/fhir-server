// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Microsoft.Health.Fhir.Core.Logging.Metrics
{
    /// <summary>
    /// Publishes metrics for health check requests.
    /// </summary>
    public interface IHealthCheckMetricPublisher
    {
        /// <summary>
        /// Publishes a metric using the supplied <see cref="HealthReport"/>.
        /// </summary>
        /// <param name="healthReport">The health check result for the current request.</param>
        void Publish(HealthReport healthReport);
    }
}
