// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Tenants
{
    /// <summary>
    /// Describes a configured tenant without allowing its settings to change at runtime.
    /// </summary>
    public sealed class TenantInfo
    {
        /// <summary>
        /// Gets the case-sensitive tenant identifier used as a URL segment.
        /// </summary>
        public string Id { get; init; }

        /// <summary>
        /// Gets the tenant's database name. Database selection is not yet tenant-aware.
        /// </summary>
        public string DatabaseName { get; init; }
    }
}
