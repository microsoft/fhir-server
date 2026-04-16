// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    /// <summary>
    /// Configuration settings for the SQL metrics watchdog.
    /// </summary>
    public class SqlMetricsWatchdogConfiguration
    {
        /// <summary>
        /// Gets or sets a value indicating whether the SQL metrics watchdog is enabled.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Gets or sets the watchdog polling period in seconds.
        /// </summary>
        public int PeriodSeconds { get; set; } = 60;
    }
}
