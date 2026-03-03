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
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Logging.Metrics;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json;

namespace Microsoft.AspNetCore.Builder
{
    public static class FhirServerApplicationBuilderExtensions
    {
        internal const string AzureTrafficManagerEndpointMonitorUserAgent = "Azure Traffic Manager Endpoint Monitor";
        private const string DataStoreHealthCheckName = "DataStoreHealthCheck";
        private const string CosmosDataStoreTag = "datastore:cosmosDB";

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

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();
            useDevelopmentIdentityProvider?.Invoke(app);
            useHttpLoggingMiddleware?.Invoke(app);

            app.UseEndpoints(
                endpoints =>
                {
                    endpoints.MapControllers();
                    endpoints.MapHealthChecks(
                        new PathString(KnownRoutes.HealthCheck),
                        new HealthCheckOptions
                        {
                            Predicate = healthCheckOptionsPredicate,
                            ResponseWriter = async (httpContext, healthReport) =>
                            {
                                PublishHealthCheckMetricIfApplicable(httpContext, healthReport);

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
                            },
                        });
                    mapAdditionalEndpoints?.Invoke(endpoints);
                });

            return app;
        }

        internal static void PublishHealthCheckMetricIfApplicable(HttpContext httpContext, HealthReport healthReport)
        {
            EnsureArg.IsNotNull(httpContext, nameof(httpContext));
            EnsureArg.IsNotNull(healthReport, nameof(healthReport));

            if (IsCosmosDbDataStoreHealthCheck(httpContext, healthReport)
                || !IsAzureTrafficManagerEndpointMonitor(httpContext.Request.Headers[HeaderNames.UserAgent]))
            {
                return;
            }

            IHealthCheckMetricPublisher healthCheckMetricPublisher = httpContext.RequestServices.GetService<IHealthCheckMetricPublisher>();
            healthCheckMetricPublisher?.Publish(healthReport);
        }

        internal static bool IsAzureTrafficManagerEndpointMonitor(StringValues userAgentHeader)
        {
            return userAgentHeader.Count == 1 && string.Equals(userAgentHeader[0], AzureTrafficManagerEndpointMonitorUserAgent, StringComparison.Ordinal);
        }

        internal static bool IsCosmosDbDataStoreHealthCheck(HttpContext httpContext, HealthReport healthReport)
        {
            // We will not publish health check metrics for Cosmos DB data store
            if (!(healthReport.Entries.Count == 1 && healthReport.Entries.ContainsKey(DataStoreHealthCheckName)))
            {
                return false;
            }

            HealthCheckServiceOptions options = httpContext.RequestServices.GetService<IOptions<HealthCheckServiceOptions>>()?.Value;
            HealthCheckRegistration registration = options?.Registrations
                .FirstOrDefault(x => string.Equals(x.Name, DataStoreHealthCheckName, StringComparison.Ordinal));

            return registration != null &&
                registration.Tags.Contains(CosmosDataStoreTag, StringComparer.OrdinalIgnoreCase);
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
