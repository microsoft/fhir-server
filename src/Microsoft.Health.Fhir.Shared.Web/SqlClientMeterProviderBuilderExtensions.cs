// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using OpenTelemetry.Metrics;

namespace Microsoft.Health.Fhir.Shared.Web
{
    /// <summary>
    /// Registers the SqlClient connection pool event counters with an OpenTelemetry meter provider.
    /// </summary>
    internal static class SqlClientMeterProviderBuilderExtensions
    {
        /// <summary>
        /// The event source that publishes the SqlClient connection pool counters.
        /// </summary>
        internal const string SqlClientEventSourceName = "Microsoft.Data.SqlClient.EventSource";

        /// <summary>
        /// Exports the SqlClient connection pool counters, such as <c>active-hard-connections</c> and <c>hard-connects</c>, through
        /// <c>OpenTelemetry.Instrumentation.EventCounters</c>.
        /// </summary>
        /// <remarks>
        /// Snapshot counters become observable gauges, and per-interval increments become cumulative observable counters.
        /// Instruments are published on the <c>OpenTelemetry.Instrumentation.EventCounters</c> meter and named
        /// <c>ec.{event source}.{counter}</c>, with the event source name shortened to keep the name within 63 characters.
        /// The counters are process-wide totals across every connection pool.
        /// </remarks>
        /// <param name="builder">The meter provider builder.</param>
        /// <returns>The same builder, for chaining.</returns>
        internal static MeterProviderBuilder AddSqlClientConnectionPoolInstrumentation(this MeterProviderBuilder builder)
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            return builder.AddEventCountersInstrumentation(options => options.AddEventSources(SqlClientEventSourceName));
        }
    }
}
