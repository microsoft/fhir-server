// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Tenancy;

namespace Microsoft.Health.Fhir.Api.Features.Tenancy
{
    /// <summary>
    /// Resolves the request tenant, acquires that tenant's container, and swaps
    /// <see cref="HttpContext.RequestServices"/> to a tenant scope for the rest of the pipeline.
    /// </summary>
    public sealed class TenantMiddleware
    {
        private const int RetryAfterSeconds = 5;

        private readonly RequestDelegate _next;
        private readonly ITenantResolver _resolver;
        private readonly ITenantRegistry _registry;
        private readonly ITenantContainerCache _cache;
        private readonly ITenantContextAccessor _tenantContextAccessor;
        private readonly ILogger<TenantMiddleware> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="TenantMiddleware"/> class.
        /// </summary>
        /// <param name="next">The next middleware in the pipeline.</param>
        /// <param name="resolver">Resolves the inbound request to a tenant identifier.</param>
        /// <param name="registry">Maps tenant identifiers to tenant descriptors.</param>
        /// <param name="cache">Acquires tenant container leases.</param>
        /// <param name="tenantContextAccessor">Stores the ambient tenant for the current async flow.</param>
        /// <param name="logger">The logger.</param>
        public TenantMiddleware(
            RequestDelegate next,
            ITenantResolver resolver,
            ITenantRegistry registry,
            ITenantContainerCache cache,
            ITenantContextAccessor tenantContextAccessor,
            ILogger<TenantMiddleware> logger)
        {
            EnsureArg.IsNotNull(next, nameof(next));
            EnsureArg.IsNotNull(resolver, nameof(resolver));
            EnsureArg.IsNotNull(registry, nameof(registry));
            EnsureArg.IsNotNull(cache, nameof(cache));
            EnsureArg.IsNotNull(tenantContextAccessor, nameof(tenantContextAccessor));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _next = next;
            _resolver = resolver;
            _registry = registry;
            _cache = cache;
            _tenantContextAccessor = tenantContextAccessor;
            _logger = logger;
        }

        /// <summary>
        /// Executes the tenant boundary middleware.
        /// </summary>
        /// <param name="context">The HTTP context.</param>
        /// <returns>A task that completes when the request has finished executing.</returns>
        public async Task InvokeAsync(HttpContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            if (!_resolver.TryResolve(context, out TenantId tenantId) ||
                !_registry.TryGetTenant(tenantId, out TenantDescriptor tenant))
            {
                _logger.LogInformation("No tenant could be resolved for host '{Host}'.", context.Request.Host.Value);
                await WriteProblemAsync(context, StatusCodes.Status404NotFound, "Unknown FHIR endpoint.").ConfigureAwait(false);
                return;
            }

            ITenantLease lease;

            try
            {
                lease = await _cache.AcquireAsync(tenant, context.RequestAborted).ConfigureAwait(false);
            }
            catch (TenantAdmissionRejectedException exception)
            {
                _logger.LogWarning(
                    exception,
                    "Shedding load for tenant {TenantId}: the resident container cap was reached.",
                    tenantId);

                context.Response.Headers.RetryAfter = RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
                await WriteProblemAsync(
                    context,
                    StatusCodes.Status503ServiceUnavailable,
                    "The server is at capacity. Please retry.").ConfigureAwait(false);
                return;
            }

            IServiceProvider originalRequestServices = context.RequestServices;
            TenantId previousTenant = _tenantContextAccessor.Current;

            try
            {
                _tenantContextAccessor.SetCurrent(tenantId);

                await using AsyncServiceScope scope = lease.Services.CreateAsyncScope();
                context.RequestServices = scope.ServiceProvider;

                await _next(context).ConfigureAwait(false);
            }
            finally
            {
                context.RequestServices = originalRequestServices;
                _tenantContextAccessor.SetCurrent(previousTenant);
                lease.Dispose();
            }
        }

        private static async Task WriteProblemAsync(HttpContext context, int statusCode, string detail)
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";

            await context.Response.WriteAsJsonAsync(new { status = statusCode, detail }).ConfigureAwait(false);
        }
    }
}
