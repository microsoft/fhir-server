// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    /// <summary>
    /// A sanitized Query Store execution plan, as emitted on a single structured log line. This is a log payload
    /// shape rather than a contract anything binds to, so it lives beside the only component that produces it and
    /// is internal.
    /// </summary>
    internal sealed class QueryPlanDiagnostics
    {
        /// <summary>
        /// Gets or sets the Query Store query identifier.
        /// </summary>
        public long QueryId { get; set; }

        /// <summary>
        /// Gets or sets the Query Store plan identifier.
        /// </summary>
        public long PlanId { get; set; }

        /// <summary>
        /// Gets or sets the sanitized query plan XML, limited to the diagnostics field-length cap.
        /// </summary>
        public string SanitizedQueryPlan { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether <see cref="SanitizedQueryPlan"/> was truncated.
        /// </summary>
        public bool QueryPlanTruncated { get; set; }

        /// <summary>
        /// Gets or sets the character length of the raw query plan XML as read from Query Store.
        /// </summary>
        public int OriginalQueryPlanLength { get; set; }

        /// <summary>
        /// Gets or sets the character length of the sanitized query plan XML before truncation.
        /// Compare this against the field cap to see how much <see cref="SanitizedQueryPlan"/> lost when
        /// <see cref="QueryPlanTruncated"/> is set. Zero when sanitization did not produce a document.
        /// </summary>
        public int SanitizedQueryPlanLength { get; set; }

        /// <summary>
        /// Gets or sets the outcome of query plan sanitization.
        /// </summary>
        public string SanitizationStatus { get; set; }

        /// <summary>
        /// Gets or sets the timestamp when the diagnostics were collected. Emitted under a name of its own rather
        /// than as <c>Timestamp</c>, because that name collides with the ingestion timestamp the log pipeline
        /// supplies for every record.
        /// </summary>
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    }
}
