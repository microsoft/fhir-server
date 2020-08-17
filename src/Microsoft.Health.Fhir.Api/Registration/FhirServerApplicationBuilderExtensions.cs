// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Health.Api.Registration;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.Routing;

namespace Microsoft.AspNetCore.Builder
{
    public static class FhirServerApplicationBuilderExtensions
    {
        /// <summary>
        /// Adds FHIR server functionality to the pipeline.
        /// </summary>
        /// <param name="app">The application builder instance.</param>
        /// <returns>THe application builder instance.</returns>
        public static IApplicationBuilder UseFhirServer(this IApplicationBuilder app)
        {
            EnsureArg.IsNotNull(app, nameof(app));

            app.UseHealthChecksExtension(new PathString(KnownRoutes.HealthCheck));

            var config = app.ApplicationServices.GetService(typeof(IOptions<FhirServerConfiguration>)) as IOptions<FhirServerConfiguration>;
            EnsureArg.IsNotNull(config, nameof(config));

            var pathBase = new PathString(config.Value.PathBase.TrimEnd('/'));
            if (pathBase.HasValue)
            {
                app.UseMiddleware<PathBaseMiddleware>(pathBase);
            }

            app.UseStaticFiles();
            app.UseMvc();

            return app;
        }

        private class PathBaseMiddleware
        {
            private readonly RequestDelegate _next;
            private readonly PathString _pathBase;

            public PathBaseMiddleware(RequestDelegate next, PathString pathBase)
            {
                if (!pathBase.HasValue)
                {
                    throw new ArgumentException($"{nameof(pathBase)} cannot be null or empty.");
                }

                _next = next ?? throw new ArgumentNullException(nameof(next));
                _pathBase = pathBase;
            }

            public async Task Invoke(HttpContext context)
            {
                if (context == null)
                {
                    throw new ArgumentNullException(nameof(context));
                }

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
