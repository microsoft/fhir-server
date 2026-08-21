// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    /// <summary>
    /// Configuration settings for the Query Store diagnostics watchdog.
    /// </summary>
    public class QueryStoreDiagnosticsConfiguration
    {
        /// <summary>
        /// Gets or sets a value indicating whether the Query Store diagnostics watchdog can run.
        /// The database runtime override must also be enabled.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Gets or sets the interval, in seconds, between diagnostics collections.
        /// </summary>
        public double PeriodSec { get; set; } = 3600;

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
    }
}
