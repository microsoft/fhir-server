// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Tenancy
{
    /// <summary>
    /// Emits the tenant name from a tenant-scoped probe injected when MVC activates the filter through a type
    /// filter.
    /// </summary>
    /// <remarks>
    /// The type is <see langword="internal"/>; MVC's <see cref="Microsoft.AspNetCore.Mvc.TypeFilterAttribute"/>
    /// activates it through <see cref="Microsoft.Extensions.DependencyInjection.ActivatorUtilities"/>, which
    /// only requires a public constructor. The attribute is not reusable, so a fresh per-request filter instance
    /// is activated for each request.
    /// </remarks>
    internal sealed class TenantIsolationProbeFilter : IAsyncActionFilter
    {
        private readonly TenantIsolationTestServerFixture.TenantScopedProbe _probe;

        /// <summary>
        /// Initializes a new instance of the <see cref="TenantIsolationProbeFilter"/> class.
        /// </summary>
        /// <param name="probe">The tenant-scoped probe injected during filter activation.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="probe"/> is <see langword="null"/>.</exception>
        public TenantIsolationProbeFilter(TenantIsolationTestServerFixture.TenantScopedProbe probe)
        {
            ArgumentNullException.ThrowIfNull(probe);

            _probe = probe;
        }

        /// <inheritdoc />
        public async Task OnActionExecutionAsync(
            ActionExecutingContext context,
            ActionExecutionDelegate next)
        {
            context.HttpContext.Response.Headers[TenantIsolationTestServerFixture.MvcActivatedFilterHeaderName] =
                _probe.TenantName;
            await next();
        }
    }
}
