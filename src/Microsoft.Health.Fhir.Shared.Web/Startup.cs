// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Features.BackgroundJobService;
using Microsoft.Health.Fhir.Azure;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Shared.Web;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.JobManagement;
using Microsoft.Health.SqlServer.Configs;

namespace Microsoft.Health.Fhir.Web
{
    public class Startup
    {
        private static string instanceId;

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public virtual void ConfigureServices(IServiceCollection services)
        {
            instanceId = $"{Configuration["WEBSITE_ROLE_INSTANCE_ID"]}--{Configuration["WEBSITE_INSTANCE_ID"]}--{Guid.NewGuid()}";

            services.AddDevelopmentIdentityProvider(Configuration);

            Core.Registration.IFhirServerBuilder fhirServerBuilder =
                services.AddFhirServer(
                    Configuration,
                    fhirServerConfiguration => fhirServerConfiguration.Security.AddAuthenticationLibrary = AddAuthenticationLibrary)
                .AddAzureExportDestinationClient()
                .AddAzureExportClientInitializer(Configuration)
                .AddContainerRegistryTokenProvider()
                .AddAzureIntegrationDataStoreClient(Configuration)
                .AddConvertData()
                .AddMemberMatch();

            string dataStore = Configuration["DataStore"];
            if (dataStore.Equals(KnownDataStores.CosmosDb, StringComparison.OrdinalIgnoreCase))
            {
                fhirServerBuilder.AddCosmosDb();
            }
            else if (dataStore.Equals(KnownDataStores.SqlServer, StringComparison.OrdinalIgnoreCase))
            {
                fhirServerBuilder.AddSqlServer(config =>
                {
                    Configuration?.GetSection(SqlServerDataStoreConfiguration.SectionName).Bind(config);
                });
                services.Configure<SqlRetryServiceOptions>(Configuration.GetSection(SqlRetryServiceOptions.SqlServer));
            }

            // Set task hosting and related background service
            if (bool.TryParse(Configuration["TaskHosting:Enabled"], out bool taskHostingsOn) && taskHostingsOn)
            {
                AddTaskHostingService(services);
            }

            // Set up Bundle Orchestrator types.
            fhirServerBuilder.AddBundleOrchestrator(enabledBundleOrchestratorOperations: false);

            /*
            The execution of IHostedServices depends on the order they are added to the dependency injection container, so we
            need to ensure that the schema is initialized before the background workers are started.
            The Export background worker is only needed in Cosmos services. In SQL it is handled by the common Job Hosting worker.
            */
            fhirServerBuilder.AddBackgroundWorkers(dataStore.Equals(KnownDataStores.CosmosDb, StringComparison.OrdinalIgnoreCase));

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

            if (bool.TryParse(Configuration["PrometheusMetrics:enabled"], out bool prometheusOn) && prometheusOn)
            {
                services.AddPrometheusMetrics(Configuration);
            }

            AddApplicationInsightsTelemetry(services);
        }

        private void AddTaskHostingService(IServiceCollection services)
        {
            services.Add<JobHosting>()
                .Scoped()
                .AsSelf();
            services.AddFactory<IScoped<JobHosting>>();

            services.AddHostedService<HostingBackgroundService>();
            services.Add<JobFactory>()
                .Scoped()
                .AsSelf()
                .AsImplementedInterfaces();
            services.Configure<TaskHostingConfiguration>(options => Configuration.GetSection("TaskHosting").Bind(options));

            IEnumerable<TypeRegistrationBuilder> jobs = services.TypesInSameAssemblyAs<ExportOrchestratorJob>()
                .AssignableTo<IJob>()
                .Transient()
                .AsSelf();

            foreach (TypeRegistrationBuilder job in jobs)
            {
                job.AsDelegate<Func<IJob>>();
            }
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public virtual void Configure(IApplicationBuilder app)
        {
            app.Use(async (context, next) =>
            {
                if (instanceId != null)
                {
                    string instanceKey = KnownHeaders.InstanceId;
                    if (!context.Response.Headers.ContainsKey(instanceKey))
                    {
                        context.Response.Headers.Add(instanceKey, new StringValues(instanceId));
                    }
                }

                await next.Invoke();
            });
            if (string.Equals(Configuration["ASPNETCORE_FORWARDEDHEADERS_ENABLED"], "true", StringComparison.OrdinalIgnoreCase))
            {
                app.UseForwardedHeaders();
            }

            app.UsePrometheusHttpMetrics();
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
                services.AddSingleton<ITelemetryInitializer, CloudRoleNameTelemetryInitializer>();
                services.AddSingleton<ITelemetryInitializer, UserAgentHeaderTelemetryInitializer>();
                services.AddLogging(loggingBuilder => loggingBuilder.AddApplicationInsights(instrumentationKey));
            }
        }

        private static void AddAuthenticationLibrary(IServiceCollection services, SecurityConfiguration securityConfiguration)
        {
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
            })
                .AddJwtBearer(options =>
                {
                    options.Authority = securityConfiguration.Authentication.Authority;
                    options.Audience = securityConfiguration.Authentication.Audience;
                    options.RequireHttpsMetadata = true;
                    options.Challenge = $"Bearer authorization_uri=\"{securityConfiguration.Authentication.Authority}\", resource_id=\"{securityConfiguration.Authentication.Audience}\", realm=\"{securityConfiguration.Authentication.Audience}\"";
                });
        }
    }
}
