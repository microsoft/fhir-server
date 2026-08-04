// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Http;
using Microsoft.Health.Fhir.Core.Features.Tenancy;

namespace Microsoft.Health.Fhir.Api.Features.Tenancy
{
    /// <summary>
    /// The default resolver, which maps every request to <see cref="TenantId.Default"/>.
    /// This preserves single-tenant behaviour for open source deployments.
    /// </summary>
    public sealed class SingleTenantResolver : ITenantResolver
    {
        /// <inheritdoc />
        public bool TryResolve(HttpContext httpContext, out TenantId tenantId)
        {
            tenantId = TenantId.Default;
            return true;
        }
    }
}
