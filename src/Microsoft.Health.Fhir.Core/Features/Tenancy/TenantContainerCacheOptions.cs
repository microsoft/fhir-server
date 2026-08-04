// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Features.Tenancy
{
    /// <summary>
    /// Configures tenant container cache residency, eviction, and sweep behavior.
    /// </summary>
    public class TenantContainerCacheOptions
    {
        /// <summary>
        /// Gets or sets the maximum number of tenant containers that may be resident at once. Requests
        /// for an additional tenant while every resident tenant is busy are rejected rather than queued.
        /// </summary>
        public int MaxResidentTenants { get; set; } = 100;

        /// <summary>
        /// Gets or sets how long an idle tenant container is retained before eviction.
        /// </summary>
        public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(30);

        /// <summary>
        /// Gets or sets how often the background sweeper runs to evict idle tenant containers.
        /// </summary>
        public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(1);
    }
}
