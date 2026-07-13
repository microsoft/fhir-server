// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net.Mime;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.Health;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Newtonsoft.Json;

namespace Microsoft.AspNetCore.Builder
{
    public static class FhirServerApplicationBuilderExtensions
    {
        /// <summary>
        /// Adds FHIR server functionality to the pipeline with health check filter.
        /// </summary>
        /// <param name="app">The application builder instance.</param>
        /// <param name="useDevelopmentIdentityProvider">The method used to register the development identity provider.</param>
        /// <param name="useHttpLoggingMiddleware">The method used to register the http logging middleware.</param>
        /// <param name="healthCheckOptionsPredicate">The predicate used to filter health check services.</param>
        /// <param name="mapAdditionalEndpoints">The method used to register additional endpoints.</param>
        /// <returns>THe application builder instance.</returns>
        public static IApplicationBuilder UseFhirServer(
            this IApplicationBuilder app,
            Func<IApplicationBuilder, IApplicationBuilder> useDevelopmentIdentityProvider = null,
            Func<IApplicationBuilder, IApplicationBuilder> useHttpLoggingMiddleware = null,
            Func<HealthCheckRegistration, bool> healthCheckOptionsPredicate = null,
            Func<IEndpointRouteBuilder, IEndpointRouteBuilder> mapAdditionalEndpoints = null)
        {
            EnsureArg.IsNotNull(app, nameof(app));

            var config = app.ApplicationServices.GetRequiredService<IOptions<FhirServerConfiguration>>();
            var pathBase = config.Value.PathBase?.TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(pathBase))
            {
                var pathString = new PathString(pathBase);
                app.UseMiddleware<PathBaseMiddleware>(pathString);
            }

            ValidateHealthCheckRegistrations(app.ApplicationServices);

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();
            useDevelopmentIdentityProvider?.Invoke(app);
            useHttpLoggingMiddleware?.Invoke(app);

            app.UseEndpoints(
                endpoints =>
                {
                    endpoints.MapControllers();

                    // Diagnostic endpoint: everything except the startup gate. Degraded => 200.
                    endpoints.MapHealthChecks(
                        new PathString(KnownRoutes.HealthCheck),
                        new HealthCheckOptions
                        {
                            Predicate = HealthProbePredicates.Diagnostic(healthCheckOptionsPredicate),
                            ResponseWriter = WriteHealthReportAsync,
                        });

                    // Startup gate: only the storage-init check. Unhealthy => 503 while initializing.
                    endpoints.MapHealthChecks(
                        new PathString(KnownRoutes.HealthCheckStartup),
                        new HealthCheckOptions
                        {
                            Predicate = HealthProbePredicates.Startup,
                            ResponseWriter = WriteHealthReportAsync,
                        });

                    // Readiness/routing: data-store + behavior. Degraded (e.g. CMK) => 200 stays routable.
                    endpoints.MapHealthChecks(
                        new PathString(KnownRoutes.HealthCheckReady),
                        new HealthCheckOptions
                        {
                            Predicate = HealthProbePredicates.Readiness,
                            ResponseWriter = WriteHealthReportAsync,
                        });

                    // Dependency-free HTTP liveness: run no checks => empty report => Healthy => 200.
                    endpoints.MapHealthChecks(
                        new PathString(KnownRoutes.HealthCheckLive),
                        new HealthCheckOptions
                        {
                            Predicate = HealthProbePredicates.Live,
                            ResponseWriter = WriteHealthReportAsync,
                        });

                    mapAdditionalEndpoints?.Invoke(endpoints);
                });

            return app;
        }

        private static async Task WriteHealthReportAsync(HttpContext httpContext, HealthReport healthReport)
        {
            var response = JsonConvert.SerializeObject(
                new
                {
                    overallStatus = healthReport.Status.ToString(),
                    details = healthReport.Entries.Select(entry => new
                    {
                        name = entry.Key,
                        status = Enum.GetName<HealthStatus>(entry.Value.Status),
                        description = entry.Value.Description,
                        data = entry.Value.Data,
                    }),
                });
            httpContext.Response.ContentType = MediaTypeNames.Application.Json;
            await httpContext.Response.WriteAsync(response).ConfigureAwait(false);
        }

        private static void ValidateHealthCheckRegistrations(IServiceProvider services)
        {
            var options = services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;

            int dataStoreCount = options.Registrations.Count(
                reg => HealthProbePredicates.Readiness(reg) && string.Equals(reg.Name, HealthCheckTags.DataStoreHealthCheckName, StringComparison.Ordinal));
            if (dataStoreCount != 1)
            {
                throw new InvalidOperationException(
                    $"Readiness probe must resolve exactly one 'DataStoreHealthCheck' registration but resolved {dataStoreCount}. " +
                    "This usually indicates a health-check tag typo or a healthcare-shared-components tag rename/package skew.");
            }

            int startupCount = options.Registrations.Count(HealthProbePredicates.Startup);
            if (startupCount != 1)
            {
                throw new InvalidOperationException(
                    $"Startup probe must resolve exactly one '{HealthCheckTags.ProbeStartup}'-tagged registration " +
                    $"(expected 'StorageInitializedHealthCheck') but resolved {startupCount}. " +
                    "This usually indicates a health-check tag typo or a missing/duplicate startup registration.");
            }
        }

        private class PathBaseMiddleware
        {
            private readonly RequestDelegate _next;
            private readonly PathString _pathBase;

            public PathBaseMiddleware(RequestDelegate next, PathString pathBase)
            {
                EnsureArg.IsNotNull(pathBase, nameof(pathBase));
                EnsureArg.IsNotNullOrWhiteSpace(pathBase.Value, nameof(pathBase.Value));

                _next = next ?? throw new ArgumentNullException(nameof(next));
                _pathBase = pathBase;
            }

            public async Task Invoke(HttpContext context)
            {
                ArgumentNullException.ThrowIfNull(context, nameof(context));

                var originalPathBase = context.Request.PathBase;
                context.Request.PathBase = originalPathBase.Add(_pathBase);

                try
                {
                    await _next(context);
                }
                finally
                {
                    context.Request.PathBase = originalPathBase;
                }
            }
        }
    }
}
