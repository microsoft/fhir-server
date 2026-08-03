// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Tenancy
{
    /// <summary>
    /// Read-only registry for tenants served by this process.
    /// </summary>
    public interface ITenantRegistry
    {
        /// <summary>
        /// Gets the tenants served by this process.
        /// </summary>
        IReadOnlyCollection<TenantDescriptor> Tenants { get; }

        /// <summary>
        /// Attempts to retrieve a tenant by identifier.
        /// </summary>
        /// <param name="tenantId">The tenant identifier to look up.</param>
        /// <param name="descriptor">The tenant descriptor when found; otherwise <c>null</c>.</param>
        /// <returns><c>true</c> when the tenant is served; otherwise <c>false</c>.</returns>
        bool TryGetTenant(TenantId tenantId, out TenantDescriptor descriptor);
    }
}
