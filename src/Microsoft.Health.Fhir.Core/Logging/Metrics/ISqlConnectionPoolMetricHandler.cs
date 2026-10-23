// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Logging.Metrics
{
    /// <summary>
    /// Reports SQL client connection pool counters for a single server instance.
    /// </summary>
    public interface ISqlConnectionPoolMetricHandler
    {
        /// <summary>
        /// Reports a sample of a SQL client connection pool counter.
        /// </summary>
        /// <param name="counterName">
        /// The SqlClient event counter name (for example <c>active-soft-connects</c>), or <c>max-pool-size</c> for the configured limit of each
        /// individual connection pool.
        /// </param>
        /// <param name="value">
        /// For gauge counters, the value when the sample was taken. For rate counters (<c>hard-connects</c>, <c>soft-connects</c>), the count
        /// during the sampling interval. SqlClient counters are process-wide totals across every connection pool (one per distinct connection
        /// string, e.g. primary, read-only replica and MergeResources), so they cannot be divided by <c>max-pool-size</c> to get utilization.
        /// </param>
        void ReportConnectionPoolCounter(string counterName, double value);
    }
}
