// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Api.Features.Health
{
    /// <summary>
    /// Configures the startup health check's transition from unhealthy to degraded.
    /// </summary>
    public class StorageInitializedHealthCheckConfiguration
    {
        /// <summary>
        /// The configuration section name.
        /// </summary>
        public const string SectionName = "HealthChecks:StorageInitialization";

        /// <summary>
        /// Gets or sets the time to report an unhealthy status while storage initializes.
        /// </summary>
        public TimeSpan StartupDegradedDelay { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Gets or sets the maximum time to wait for storage initialization before checking CMK health.
        /// </summary>
        public TimeSpan StorageInitializationTimeout { get; set; } = TimeSpan.FromMinutes(5);
    }
}
