// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    /// <summary>
    /// Configuration options for CleanupEventLogWatchdog.
    /// </summary>
    public class CleanupEventLogWatchdogOptions
    {
        /// <summary>Used to bind this class to configuration data source.</summary>
        public const string SectionName = "SqlServer:CleanupEventLogWatchdog";

        /// <summary>
        /// Enables or disables the LogRawResourceStats functionality.
        /// Default is false (disabled) for performance reasons.
        /// </summary>
        public bool LogRawResourceStatsEnabled { get; set; } = false;

        /// <summary>
        /// Batch size for processing raw resources in LogRawResourceStats.
        /// Default is 10000. Can be reduced if experiencing CPU/memory issues.
        /// </summary>
        public int LogRawResourceStatsBatchSize { get; set; } = 10000;
    }
}