// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Api.Configs
{
    /// <summary>
    /// Controls the optional multi-tenant hosting mode, where one process serves multiple tenants with
    /// separate dependency injection containers.
    /// </summary>
    public class TenancyConfiguration
    {
        /// <summary>
        /// Gets or sets a value indicating whether multi-tenant hosting is enabled.
        /// </summary>
        /// <remarks>
        /// The default is <see langword="false"/>, which preserves the existing single-tenant behavior.
        /// </remarks>
        public bool Enabled { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of tenant containers that may be resident at the same time.
        /// </summary>
        public int MaxResidentTenants { get; set; } = 100;

        /// <summary>
        /// Gets or sets how long an idle tenant container is retained before eviction.
        /// </summary>
        public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(30);

        /// <summary>
        /// Gets or sets how often idle tenant containers are swept for eviction.
        /// </summary>
        public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(1);
    }
}
