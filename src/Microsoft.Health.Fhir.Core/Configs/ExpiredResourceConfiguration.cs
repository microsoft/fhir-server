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
        /// <summary>
        /// Gets or sets a value indicating whether the expired resource cleanup watchdog is enabled.
        /// </summary>
        public bool Enabled { get; set; } = false;
    }
}
