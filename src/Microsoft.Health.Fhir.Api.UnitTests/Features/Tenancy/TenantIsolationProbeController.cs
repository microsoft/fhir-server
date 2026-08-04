// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Tenancy
{
    /// <summary>
    /// Exercises MVC controller activation and action invocation after tenant middleware has selected
    /// the request's tenant service provider.
    /// </summary>
    [ApiController]
    [Route("mvc")]
    public sealed class TenantIsolationProbeController : ControllerBase
    {
        private readonly TenantIsolationTestServerFixture.TenantScopedProbe _probe;

        /// <summary>
        /// Initializes a new instance of the <see cref="TenantIsolationProbeController"/> class.
        /// </summary>
        /// <param name="probe">The tenant-scoped probe injected during controller activation.</param>
        public TenantIsolationProbeController(TenantIsolationTestServerFixture.TenantScopedProbe probe)
        {
            _probe = probe;
        }

        /// <summary>
        /// Gets the tenant-scoped probe injected during controller activation.
        /// </summary>
        /// <returns>The tenant name from the probe injected during controller activation.</returns>
        [HttpGet("whoami")]
        public IActionResult GetTenant()
        {
            return Ok(new { tenant = _probe.TenantName });
        }

        /// <summary>
        /// Gets the tenant-scoped probe injected during controller activation.
        /// </summary>
        /// <returns>The tenant name from the probe injected during controller activation.</returns>
        [HttpGet("constructor")]
        public IActionResult GetConstructorTenant()
        {
            return Ok(new { tenant = _probe.TenantName });
        }

        /// <summary>
        /// Gets bound route and query values with a tenant-scoped probe injected into an action parameter.
        /// </summary>
        /// <param name="routeValue">The value bound from the route.</param>
        /// <param name="queryValue">The value bound from the query string.</param>
        /// <param name="probe">The tenant-scoped probe injected into the action parameter.</param>
        /// <returns>The tenant name and bound route and query values.</returns>
        [HttpGet("from-services/{routeValue}")]
        public IActionResult GetFromServices(
            [FromRoute] string routeValue,
            [FromQuery(Name = "queryValue")] string queryValue,
            [FromServices] TenantIsolationTestServerFixture.TenantScopedProbe probe)
        {
            return Ok(new { tenant = probe.TenantName, routeValue, queryValue });
        }

        /// <summary>
        /// Gets the controller-injected tenant probe after a per-request type filter is activated.
        /// </summary>
        /// <param name="routeValue">The value bound from the route.</param>
        /// <returns>The tenant name and bound route value.</returns>
        [HttpGet("type-filter/{routeValue}")]
        [TypeFilter(typeof(TenantIsolationProbeFilter))]
        public IActionResult GetTypeFilterTenant([FromRoute] string routeValue)
        {
            return Ok(new { tenant = _probe.TenantName, routeValue });
        }

        /// <summary>
        /// Gets the controller-injected tenant probe after JWT authentication has completed.
        /// </summary>
        /// <returns>The tenant name from the controller-injected probe.</returns>
        [Authorize]
        [HttpGet("secure")]
        public IActionResult GetSecureTenant()
        {
            return Ok(new { tenant = _probe.TenantName });
        }
    }
}
