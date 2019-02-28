// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net.Mime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Health.ControlPlane.CosmosDb.Health;
using Microsoft.Health.Fhir.CosmosDb.Features.Health;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Web
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public virtual void ConfigureServices(IServiceCollection services)
        {
            services.AddControlPlaneCosmosDb(Configuration).AddDevelopmentIdentityProvider(Configuration);

            services.AddFhirServer(Configuration).AddFhirServerCosmosDb(Configuration);

            services.AddHealthChecks()
                .AddCheck<FhirCosmosHealthCheck>(name: "FhirCosmosDb", failureStatus: HealthStatus.Unhealthy)
                .AddCheck<ControlPlaneCosmosHealthCheck>(name: "ControlPlaneCosmosDb", failureStatus: HealthStatus.Unhealthy);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public virtual void Configure(IApplicationBuilder app)
        {
            app.UseFhirServer();

            app.UseDevelopmentIdentityProvider();

            app.UseHealthChecks("/health", new HealthCheckOptions
            {
                ResponseWriter = async (context, report) =>
                {
                    var result = JsonConvert.SerializeObject(
                        new
                        {
                            status = report.Status.ToString(),
                            details = report.Entries.Select(e => new { key = e.Key, value = Enum.GetName(typeof(HealthStatus), e.Value.Status) }),
                        });
                    context.Response.ContentType = MediaTypeNames.Application.Json;
                    await context.Response.WriteAsync(result);
                },
            });
        }
    }
}
