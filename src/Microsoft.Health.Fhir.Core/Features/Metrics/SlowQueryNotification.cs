// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Features.Metrics
{
    /// <summary>
    /// Contains aggregated Query Store metrics for a slow query plan.
    /// </summary>
    public class SlowQueryNotification : IMetricsNotification
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
        /// Gets or sets the number of regular completed executions in the reporting interval.
        /// </summary>
        public long ExecutionCount { get; set; }

        /// <summary>
        /// Gets or sets the total execution duration, in milliseconds.
        /// </summary>
        public double TotalDurationMilliseconds { get; set; }

        /// <summary>
        /// Gets or sets the weighted average execution duration, in milliseconds.
        /// </summary>
        public double AverageDurationMilliseconds { get; set; }

        /// <summary>
        /// Gets or sets the maximum execution duration, in milliseconds.
        /// </summary>
        public double MaxDurationMilliseconds { get; set; }

        /// <summary>
        /// Gets or sets the total CPU time, in milliseconds.
        /// </summary>
        public double TotalCpuMilliseconds { get; set; }

        /// <summary>
        /// Gets or sets the weighted average CPU time, in milliseconds.
        /// </summary>
        public double AverageCpuMilliseconds { get; set; }

        /// <summary>
        /// Gets or sets the total logical reads.
        /// </summary>
        public double TotalLogicalReads { get; set; }

        /// <summary>
        /// Gets or sets the weighted average logical reads.
        /// </summary>
        public double AverageLogicalReads { get; set; }

        /// <summary>
        /// Gets or sets the total observed wait time, in milliseconds, when Query Store wait statistics are available.
        /// </summary>
        public double? TotalWaitMilliseconds { get; set; }

        /// <summary>
        /// Gets or sets the average observed wait time per execution, in milliseconds, when Query Store wait statistics are available.
        /// </summary>
        public double? AverageWaitMilliseconds { get; set; }

        /// <summary>
        /// Gets or sets the wait category with the greatest observed wait time, when Query Store wait statistics are available.
        /// </summary>
        public string TopWaitCategory { get; set; }

        /// <summary>
        /// Gets or sets the outcome of wait-statistics collection, so that absent wait fields are self-describing:
        /// <c>Available</c> when wait statistics were read for this plan, <c>Unavailable</c> when the wait query
        /// succeeded but returned no row for this plan (typically wait capture is off, or the plan accrued no waits),
        /// and <c>Failed</c> when the wait query itself threw. <c>Failed</c> means the wait fields are missing because
        /// collection is broken, not because there was nothing to report.
        /// </summary>
        public string WaitStatisticsStatus { get; set; }

        /// <summary>
        /// Gets or sets the query text, limited to the diagnostics field-length cap.
        /// </summary>
        public string QueryText { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether <see cref="QueryText"/> was truncated.
        /// </summary>
        public bool QueryTextTruncated { get; set; }

        /// <summary>
        /// Gets or sets the character length of the query text before truncation, so the amount lost is
        /// visible when <see cref="QueryTextTruncated"/> is set.
        /// </summary>
        public int QueryTextLength { get; set; }

        /// <summary>
        /// Gets or sets the start of the Query Store reporting interval.
        /// </summary>
        public DateTimeOffset IntervalStart { get; set; }

        /// <summary>
        /// Gets or sets the end of the Query Store reporting interval.
        /// </summary>
        public DateTimeOffset IntervalEnd { get; set; }

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
