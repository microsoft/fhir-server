// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Azure;
using Prometheus;
using Prometheus.SystemMetrics;

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
            services.AddDevelopmentIdentityProvider(Configuration);

            Core.Registration.IFhirServerBuilder fhirServerBuilder = services.AddFhirServer(Configuration)
                .AddBackgroundWorkers()
                .AddAzureExportDestinationClient()
                .AddAzureExportClientInitializer(Configuration);

            string dataStore = Configuration["DataStore"];
            if (dataStore.Equals(KnownDataStores.CosmosDb, StringComparison.InvariantCultureIgnoreCase))
            {
                fhirServerBuilder.AddCosmosDb(Configuration);
            }
            else if (dataStore.Equals(KnownDataStores.SqlServer, StringComparison.InvariantCultureIgnoreCase))
            {
                fhirServerBuilder.AddSqlServer();
            }

            if (string.Equals(Configuration["ASPNETCORE_FORWARDEDHEADERS_ENABLED"], "true", StringComparison.OrdinalIgnoreCase))
            {
                services.Configure<ForwardedHeadersOptions>(options =>
                {
                    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor |
                        ForwardedHeaders.XForwardedProto;

                    // Only loopback proxies are allowed by default.
                    // Clear that restriction because forwarders are enabled by explicit
                    // configuration.
                    options.KnownNetworks.Clear();
                    options.KnownProxies.Clear();
                });
            }

            PrometheusMetricsConfig prometheusConfig = new PrometheusMetricsConfig();
            Configuration.Bind("PrometheusMetrics", prometheusConfig);
            services.AddSingleton(Options.Create(prometheusConfig));

            if (prometheusConfig.Enabled)
            {
                services.Add<PrometheusMetricsServer>()
                    .Singleton()
                    .AsService<IStartable>();

                if (prometheusConfig.SystemMetrics)
                {
                    services.AddSystemMetrics();
                }
            }

            AddApplicationInsightsTelemetry(services);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public virtual void Configure(IApplicationBuilder app, ILogger<Startup> logger, IOptions<PrometheusMetricsConfig> prometheusConfig)
        {
            if (string.Equals(Configuration["ASPNETCORE_FORWARDEDHEADERS_ENABLED"], "true", StringComparison.OrdinalIgnoreCase))
            {
                app.UseForwardedHeaders();
            }

            var prometheusMetricsConfig = prometheusConfig.Value;
            if (prometheusMetricsConfig.Enabled)
            {
                if (prometheusMetricsConfig.HttpMetrics)
                {
                    app.UseHttpMetrics();
                }
            }

            app.UseFhirServer();
            app.UseDevelopmentIdentityProviderIfConfigured();
        }

        /// <summary>
        /// Adds ApplicationInsights for telemetry and logging.
        /// </summary>
        private void AddApplicationInsightsTelemetry(IServiceCollection services)
        {
            string instrumentationKey = Configuration["ApplicationInsights:InstrumentationKey"];

            if (!string.IsNullOrWhiteSpace(instrumentationKey))
            {
                services.AddApplicationInsightsTelemetry(instrumentationKey);
                services.AddLogging(loggingBuilder => loggingBuilder.AddApplicationInsights(instrumentationKey));
            }
        }
    }
}
