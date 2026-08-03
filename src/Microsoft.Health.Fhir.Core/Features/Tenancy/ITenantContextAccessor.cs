// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Tenancy
{
    /// <summary>
    /// Provides ambient access to the tenant associated with the current logical operation.
    /// </summary>
    /// <remarks>
    /// Implementations must flow values across <c>await</c> boundaries while isolating sibling logical flows.
    /// </remarks>
    public interface ITenantContextAccessor
    {
        /// <summary>
        /// Gets the tenant associated with the current logical operation.
        /// </summary>
        TenantId Current { get; }

        /// <summary>
        /// Sets the tenant associated with the current logical operation.
        /// </summary>
        /// <param name="tenantId">The tenant to make current.</param>
        void SetCurrent(TenantId tenantId);
    }
}
