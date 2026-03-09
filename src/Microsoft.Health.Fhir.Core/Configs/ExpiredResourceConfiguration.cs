// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Messages.Delete;

namespace Microsoft.Health.Fhir.Core.Configs
{
    /// <summary>
    /// Configuration settings for the expired resource cleanup watchdog.
    /// </summary>
    public class ExpiredResourceConfiguration
    {
        private const int DefaultRetentionPeriodDays = 90;

        /// <summary>
        /// Gets or sets a value indicating whether the expired resource cleanup watchdog is enabled.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Gets or sets the retention period in days. Resources not updated within this period will be deleted.
        /// </summary>
        public int RetentionPeriodDays { get; set; } = DefaultRetentionPeriodDays;
    }
}
