// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Tenancy
{
    /// <summary>
    /// The default <see cref="ITenantRegistry"/> implementation for a single-tenant process.
    /// </summary>
    public sealed class SingleTenantRegistry : ITenantRegistry
    {
        private static readonly TenantDescriptor DefaultTenant = new TenantDescriptor(TenantId.Default);

        private static readonly IReadOnlyCollection<TenantDescriptor> DefaultTenants =
            System.Array.AsReadOnly(new[] { DefaultTenant });

        /// <summary>
        /// Gets the tenants served by this process.
        /// </summary>
        public IReadOnlyCollection<TenantDescriptor> Tenants { get; } = DefaultTenants;

        /// <summary>
        /// Attempts to retrieve a tenant by identifier.
        /// </summary>
        /// <param name="tenantId">The tenant identifier to look up.</param>
        /// <param name="descriptor">The tenant descriptor when found; otherwise <c>null</c>.</param>
        /// <returns><c>true</c> when the tenant is served; otherwise <c>false</c>.</returns>
        public bool TryGetTenant(TenantId tenantId, out TenantDescriptor descriptor)
        {
            if (tenantId == TenantId.Default)
            {
                descriptor = DefaultTenant;
                return true;
            }

            descriptor = null;
            return false;
        }
    }
}
