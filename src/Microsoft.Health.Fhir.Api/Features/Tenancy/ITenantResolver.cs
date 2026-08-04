// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Http;
using Microsoft.Health.Fhir.Core.Features.Tenancy;

namespace Microsoft.Health.Fhir.Api.Features.Tenancy
{
    /// <summary>
    /// Determines which tenant an inbound HTTP request belongs to.
    /// </summary>
    public interface ITenantResolver
    {
        /// <summary>
        /// Attempts to determine the tenant for the supplied request.
        /// </summary>
        /// <param name="httpContext">The inbound request.</param>
        /// <param name="tenantId">When this method returns <c>true</c>, contains the resolved tenant identifier.</param>
        /// <returns><c>true</c> if a tenant could be determined; otherwise <c>false</c>.</returns>
        bool TryResolve(HttpContext httpContext, out TenantId tenantId);
    }
}
