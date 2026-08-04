// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Tenancy
{
    /// <summary>
    /// Exercises MVC controller activation and action invocation after tenant middleware has replaced
    /// <see cref="Microsoft.AspNetCore.Http.HttpContext.RequestServices"/>.
    /// </summary>
    [ApiController]
    [Route("mvc")]
    public sealed class TenantIsolationProbeController : ControllerBase
    {
        /// <summary>
        /// Resolves the current tenant's scoped probe through the request service provider.
        /// </summary>
        /// <returns>The tenant name resolved for this request.</returns>
        [HttpGet("whoami")]
        public IActionResult GetTenant()
        {
            TenantIsolationTestServerFixture.TenantScopedProbe probe =
                HttpContext.RequestServices.GetRequiredService<TenantIsolationTestServerFixture.TenantScopedProbe>();

            return Ok(new { tenant = probe.TenantName });
        }

        /// <summary>
        /// Resolves the current tenant's scoped probe after JWT authentication has completed.
        /// </summary>
        /// <returns>The tenant name resolved for this request.</returns>
        [Authorize]
        [HttpGet("secure")]
        public IActionResult GetSecureTenant()
        {
            TenantIsolationTestServerFixture.TenantScopedProbe probe =
                HttpContext.RequestServices.GetRequiredService<TenantIsolationTestServerFixture.TenantScopedProbe>();

            return Ok(new { tenant = probe.TenantName });
        }
    }
}
