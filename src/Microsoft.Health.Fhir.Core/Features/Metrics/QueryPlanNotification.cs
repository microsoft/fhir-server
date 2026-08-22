// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Features.Metrics
{
    /// <summary>
    /// Contains a sanitized Query Store execution plan.
    /// </summary>
    public class QueryPlanNotification : IMetricsNotification
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
        /// Gets or sets the timestamp when the notification was created.
        /// </summary>
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Gets the FHIR operation associated with this notification.
        /// </summary>
        public string FhirOperation => "query-store-diagnostics";

        /// <summary>
        /// Gets the resource type associated with this notification.
        /// </summary>
        public string ResourceType => "System";
    }
}
