// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Configs
{
    /// <summary>
    /// Configuration settings for the Query Store diagnostics watchdog.
    /// </summary>
    public class QueryStoreDiagnosticsConfiguration
    {
        /// <summary>
        /// Gets or sets a value indicating whether the Query Store diagnostics watchdog runs. This is the only
        /// switch: the feature has no database-side enablement and writes no configuration to the database.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Gets or sets the interval, in seconds, between diagnostics collections. Also used, clamped to
        /// [60, 86400], as the Query Store lookback window each collection covers.
        /// </summary>
        public double PeriodSec { get; set; } = 3600;

        /// <summary>
        /// Gets or sets the lease renewal interval, in seconds, used to elect the single replica that collects each
        /// period. Must be positive: it is handed to the lease timer, which rejects a non-positive value. The
        /// watchdog base class writes this value into <c>dbo.Parameters</c>, so it is exposed here to keep the
        /// stored row settable from an environment variable.
        /// </summary>
        public double LeasePeriodSec { get; set; } = 600;

        /// <summary>
        /// Gets or sets the maximum number of slow query plans reported per collection.
        /// </summary>
        public int SlowQueryCount { get; set; } = 10;

        /// <summary>
        /// Gets or sets the minimum weighted average plan duration, in milliseconds, to report.
        /// </summary>
        public int MinDurationMilliseconds { get; set; } = 1000;

        /// <summary>
        /// Gets or sets a value indicating whether sanitized query plans are reported.
        /// </summary>
        public bool IncludeQueryPlans { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether table statistics health is reported.
        /// </summary>
        public bool IncludeStatisticsHealth { get; set; } = true;

        /// <summary>
        /// Gets or sets the maximum number of table statistics rows reported per collection.
        /// </summary>
        public int StatisticsHealthCount { get; set; } = 20;

        /// <summary>
        /// Gets or sets the number of table statistics rows packed into each emitted log line. This is not a cap on
        /// what is collected — <see cref="StatisticsHealthCount"/> is — only on how many of the collected rows share
        /// a line. Rows beyond the batch size are emitted on further lines, each carrying its page number and the
        /// total page and row counts. A non-positive value is reported and the default is used, because a batch size
        /// cannot pack a row.
        /// </summary>
        public int StatisticsHealthBatchSize { get; set; } = 20;

        /// <summary>
        /// Gets or sets the inclusive start of the run window, before which no diagnostics are collected.
        /// Null, the default, means there is no lower bound and collection can begin immediately.
        /// A value without an explicit UTC offset is interpreted in the host's local timezone, which is rarely
        /// what was intended and is not visible in the configured text, so an ISO-8601 value with a <c>Z</c>
        /// suffix such as <c>2026-03-01T00:00:00Z</c> is recommended.
        /// </summary>
        public DateTimeOffset? RunStartDate { get; set; }

        /// <summary>
        /// Gets or sets the exclusive end of the run window, at and after which no diagnostics are collected.
        /// Null, the default, means there is no upper bound and collection continues indefinitely.
        /// A value without an explicit UTC offset is interpreted in the host's local timezone, which is rarely
        /// what was intended and is not visible in the configured text, so an ISO-8601 value with a <c>Z</c>
        /// suffix such as <c>2026-03-08T00:00:00Z</c> is recommended.
        /// </summary>
        public DateTimeOffset? RunEndDate { get; set; }
    }
}
