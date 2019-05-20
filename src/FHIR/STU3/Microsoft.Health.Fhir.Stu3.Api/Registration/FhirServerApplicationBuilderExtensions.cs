// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net.Mime;
using EnsureThat;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Newtonsoft.Json;

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

            app.UseHealthChecks("/health/check", new HealthCheckOptions
            {
                ResponseWriter = async (httpContext, healthReport) =>
                {
                    var response = JsonConvert.SerializeObject(
                        new
                        {
                            overallStatus = healthReport.Status.ToString(),
                            details = healthReport.Entries.Select(entry => new
                            {
                                name = entry.Key,
                                status = Enum.GetName(typeof(HealthStatus), entry.Value.Status),
                            }),
                        });

                    httpContext.Response.ContentType = MediaTypeNames.Application.Json;
                    await httpContext.Response.WriteAsync(response);
                },
            });

            app.UseStaticFiles();
            app.UseMvc();

            return app;
        }
    }
}
