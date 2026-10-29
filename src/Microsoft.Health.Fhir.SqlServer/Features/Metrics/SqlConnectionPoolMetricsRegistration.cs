// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Data.SqlClient;
using OpenTelemetry.Metrics;

namespace Microsoft.Health.Fhir.SqlServer.Features.Metrics
{
    /// <summary>
    /// Configures SqlClient connection pool instrumentation for a host's OpenTelemetry metrics pipeline.
    /// </summary>
    public static class SqlConnectionPoolMetricsRegistration
    {
        /// <summary>
        /// Adds SqlClient EventCounters and the configured pool maximum to the metrics pipeline.
        /// </summary>
        /// <param name="builder">The host's metrics builder.</param>
        /// <param name="refreshIntervalSeconds">The EventCounter sampling interval, in seconds.</param>
        /// <returns>The metrics builder.</returns>
        public static MeterProviderBuilder AddSqlConnectionPoolMetrics(this MeterProviderBuilder builder, int refreshIntervalSeconds = 10)
        {
            // Work around SqlClient 7.1.0 by initializing its metrics callback before enabling EventCounters.
            new SqlConnection().Dispose();

            return builder
                .AddMeter(SqlConnectionPoolConfigurationMetrics.MeterName)
                .AddEventCountersInstrumentation(options =>
                {
                    options.RefreshIntervalSecs = refreshIntervalSeconds;
                    options.AddEventSources("Microsoft.Data.SqlClient.EventSource");
                });
        }
    }
}
