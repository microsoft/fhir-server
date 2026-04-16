// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    /// <summary>
    /// Configuration settings for watchdog background services.
    /// </summary>
    public class WatchdogConfiguration
    {
        /// <summary>
        /// Gets the expired resource cleanup configuration.
        /// </summary>
        public ExpiredResourceConfiguration ExpiredResource { get; } = new ExpiredResourceConfiguration();

        /// <summary>
        /// Gets the SQL metrics watchdog configuration.
        /// </summary>
        public SqlMetricsWatchdogConfiguration SqlMetrics { get; } = new SqlMetricsWatchdogConfiguration();
    }
}
