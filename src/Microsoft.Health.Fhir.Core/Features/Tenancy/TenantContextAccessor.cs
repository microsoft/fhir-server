// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;

namespace Microsoft.Health.Fhir.Core.Features.Tenancy
{
    /// <summary>
    /// An <see cref="AsyncLocal{T}"/>-backed implementation of <see cref="ITenantContextAccessor"/>.
    /// </summary>
    /// <remarks>
    /// The stored tenant follows the current logical call context, so a singleton registration is safe.
    /// </remarks>
    public class TenantContextAccessor : ITenantContextAccessor
    {
        private readonly AsyncLocal<TenantId> _currentTenant = new();

        /// <inheritdoc />
        public TenantId Current => _currentTenant.Value;

        /// <inheritdoc />
        public void SetCurrent(TenantId tenantId) => _currentTenant.Value = tenantId;
    }
}
