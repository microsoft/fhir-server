// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Linq;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Medino;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Features.BackgroundJobService;
using Microsoft.Health.Fhir.Api.Modules;
using Microsoft.Health.Fhir.Api.OpenIddict.Extensions;
using Microsoft.Health.Fhir.Api.OpenIddict.FeatureProviders;
using Microsoft.Health.Fhir.Azure;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Telemetry;
using Microsoft.Health.Fhir.Core.Logging.Metrics;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Messages.Storage;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.Shared.Web;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.JobManagement;
using Microsoft.Health.SqlServer.Configs;
using Microsoft.Net.Http.Headers;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using TelemetryConfiguration = Microsoft.Health.Fhir.Core.Configs.TelemetryConfiguration;

namespace Microsoft.Health.Fhir.Web
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "Internal framework instantiation.")]
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

            Core.Registration.IFhirServerBuilder fhirServerBuilder =
                services.AddFhirServer(
                    Configuration,
                    fhirServerConfiguration => fhirServerConfiguration.Security.AddAuthenticationLibrary = AddAuthenticationLibrary,
                    mvcBuilderAction: builder =>
                    {
                        builder.PartManager.FeatureProviders.Remove(builder.PartManager.FeatureProviders.OfType<ControllerFeatureProvider>().FirstOrDefault());
                        builder.PartManager.FeatureProviders.Add(new FhirControllerFeatureProvider(Configuration));
                    })
                .AddAzureExportDestinationClient()
                .AddAzureExportClientInitializer(Configuration)
                .AddContainerRegistryTokenProvider()
                .AddContainerRegistryAccessValidator()
                .AddAzureIntegrationDataStoreClient(Configuration)
                .AddConvertData()
                .AddMemberMatch();

            services.AddDevelopmentIdentityProvider(Configuration);

            // Set the runtime configuration for the up and running service.
            IFhirRuntimeConfiguration runtimeConfiguration = AddRuntimeConfiguration(Configuration, fhirServerBuilder);

            AddDataStore(services, fhirServerBuilder, runtimeConfiguration);

            // Set task hosting and related background service
            if (bool.TryParse(Configuration["TaskHosting:Enabled"], out bool taskHostingsOn) && taskHostingsOn)
            {
                AddTaskHostingService(services);
            }

            // Set up Bundle Orchestrator.
            fhirServerBuilder.AddBundleOrchestrator(Configuration);

            if (bool.TryParse(Configuration["PrometheusMetrics:enabled"], out bool prometheusOn) && prometheusOn)
            {
                services.AddPrometheusMetrics(Configuration);
            }

            AddTelemetryProvider(services);
        }

        private void AddDataStore(IServiceCollection services, IFhirServerBuilder fhirServerBuilder, IFhirRuntimeConfiguration runtimeConfiguration)
        {
            if (runtimeConfiguration is AzureApiForFhirRuntimeConfiguration)
            {
                fhirServerBuilder.AddCosmosDb();
            }
            else if (runtimeConfiguration is AzureHealthDataServicesRuntimeConfiguration)
            {
                fhirServerBuilder.AddSqlServer(config =>
                {
                    Configuration?.GetSection(SqlServerDataStoreConfiguration.SectionName).Bind(config);
                });
                services.Configure<SqlRetryServiceOptions>(Configuration.GetSection(SqlRetryServiceOptions.SqlServer));
            }
        }

        private IFhirRuntimeConfiguration AddRuntimeConfiguration(IConfiguration configuration, IFhirServerBuilder fhirServerBuilder)
        {
            IFhirRuntimeConfiguration runtimeConfiguration = null;

            string dataStore = Configuration["DataStore"];
            if (KnownDataStores.IsCosmosDbDataStore(dataStore))
            {
                runtimeConfiguration = new AzureApiForFhirRuntimeConfiguration();
            }
            else if (KnownDataStores.IsSqlServerDataStore(dataStore))
            {
                runtimeConfiguration = new AzureHealthDataServicesRuntimeConfiguration();
            }
            else
            {
                throw new InvalidOperationException($"Invalid data store type '{dataStore}'.");
            }

            fhirServerBuilder.Services.AddSingleton<IFhirRuntimeConfiguration>(runtimeConfiguration);

            return runtimeConfiguration;
        }

        private void AddTaskHostingService(IServiceCollection services)
        {
            services.Add<JobHosting>()
                .Scoped()
                .AsSelf();
            services.AddFactory<IScoped<JobHosting>>();

            services.RemoveServiceTypeExact<HostingBackgroundService, INotificationHandler<SearchParametersInitializedNotification>>()
                .Add<HostingBackgroundService>()
                .Singleton()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<JobFactory>()
                .Scoped()
                .AsSelf()
                .AsImplementedInterfaces();
            services.Configure<TaskHostingConfiguration>(options => Configuration.GetSection("TaskHosting").Bind(options));

            IEnumerable<TypeRegistrationBuilder> jobs = services.TypesInSameAssembly(KnownAssemblies.Core)
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
                        context.Response.Headers[instanceKey] = new StringValues(instanceId);
                    }
                }

                await next.Invoke();
            });

            app.UsePrometheusHttpMetrics();
            app.UseFhirServer(DevelopmentIdentityProviderRegistrationExtensions.UseDevelopmentIdentityProviderIfConfigured);
        }

        /// <summary>
        /// Adds ApplicationInsights for telemetry and logging.
        /// </summary>
        private static void AddApplicationInsightsTelemetry(IServiceCollection services, TelemetryConfiguration configuration)
        {
            if (configuration.Provider == TelemetryProvider.ApplicationInsights
                && (!string.IsNullOrWhiteSpace(configuration.InstrumentationKey) || !string.IsNullOrWhiteSpace(configuration.ConnectionString)))
            {
                services.AddHttpContextAccessor();
                services.AddApplicationInsightsTelemetry(options =>
                {
                    if (!string.IsNullOrWhiteSpace(configuration.InstrumentationKey))
                    {
#pragma warning disable CS0618 // Type or member is obsolete
                        options.InstrumentationKey = configuration.InstrumentationKey;
#pragma warning restore CS0618 // Type or member is obsolete
                    }
                    else
                    {
                        options.ConnectionString = configuration.ConnectionString;
                    }
                });
                services.AddSingleton<ITelemetryInitializer, CloudRoleNameTelemetryInitializer>();
                services.AddSingleton<ITelemetryInitializer, UserAgentHeaderTelemetryInitializer>();
                services.AddLogging(loggingBuilder =>
                {
                    loggingBuilder.AddApplicationInsights(
                        options =>
                        {
                            if (!string.IsNullOrWhiteSpace(configuration.InstrumentationKey))
                            {
#pragma warning disable CS0618 // Type or member is obsolete
                                options.InstrumentationKey = configuration.InstrumentationKey;
#pragma warning restore CS0618 // Type or member is obsolete
                            }
                            else
                            {
                                options.ConnectionString = configuration.ConnectionString;
                            }
                        },
                        options =>
                        {
                        });
                });
            }
        }

        private static void AddAuthenticationLibrary(IServiceCollection services, SecurityConfiguration securityConfiguration)
        {
            // Note: This method is still used to configure JWT Bearer for non–OpenIddict tokens.
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
                options.TokenValidationParameters.RoleClaimType = securityConfiguration.Authorization.RolesClaim;
                options.MapInboundClaims = false;
                options.RequireHttpsMetadata = true;
                options.Challenge = $"Bearer authorization_uri=\"{securityConfiguration.Authentication.Authority}\", resource_id=\"{securityConfiguration.Authentication.Audience}\", realm=\"{securityConfiguration.Authentication.Audience}\"";
            });
        }

        /// <summary>
        /// Adds AzureMonitorOpenTelemetry for telemetry and logging.
        /// </summary>
        private static void AddAzureMonitorOpenTelemetry(IServiceCollection services, TelemetryConfiguration configuration)
        {
            if (configuration.Provider == TelemetryProvider.OpenTelemetry && !string.IsNullOrWhiteSpace(configuration.ConnectionString))
            {
                services.AddHttpContextAccessor();
                services.AddOpenTelemetry()
                    .UseAzureMonitor(options =>
                    {
                        options.ConnectionString = configuration.ConnectionString;
                    })
                    .ConfigureResource(builder =>
                    {
                        var resourceAttributes = new Dictionary<string, object>()
                        {
                            { "service.name", "Microsoft FHIR Server" },
                        };

                        builder.AddAttributes(resourceAttributes);
                    });
                services.Configure<AspNetCoreTraceInstrumentationOptions>(options =>
                    {
                        options.RecordException = true;
                        options.EnrichWithHttpRequest = (activity, request) =>
                        {
                            if (request.Headers.TryGetValue(HeaderNames.UserAgent, out var userAgent))
                            {
                                string propertyName = HeaderNames.UserAgent.Replace('-', '_').ToLower(CultureInfo.InvariantCulture);
                                activity?.SetTag(propertyName, userAgent);
                            }
                        };
                        options.EnrichWithHttpResponse = (activity, response) =>
                        {
                            var request = response?.HttpContext?.Request;
                            if (request != null)
                            {
                                var name = request.Path.Value;
                                if (request.RouteValues != null
                                    && request.RouteValues.TryGetValue(KnownHttpRequestProperties.RouteValueAction, out var action)
                                    && request.RouteValues.TryGetValue(KnownHttpRequestProperties.RouteValueController, out var controller))
                                {
                                    name = $"{controller}/{action}";
                                    var parameterArray = request.RouteValues.Keys?.Where(
                                        k => k.Contains(KnownHttpRequestProperties.RouteValueParameterSuffix, StringComparison.OrdinalIgnoreCase))
                                        .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                                        .ToArray();
                                    if (parameterArray != null && parameterArray.Any())
                                    {
                                        name += $" [{string.Join("/", parameterArray)}]";
                                    }
                                }

                                if (!string.IsNullOrWhiteSpace(name))
                                {
                                    activity?.SetTag(KnownApplicationInsightsDimensions.OperationName, $"{request.Method} {name}");
                                }
                            }
                        };
                    });
                services.Configure<OpenTelemetryLoggerOptions>(options =>
                    {
                        options.AddProcessor(sp =>
                        {
                            var httpContextAccessor = sp.GetRequiredService<IHttpContextAccessor>();
                            var failureMetricHandler = sp.GetRequiredService<IFailureMetricHandler>();
                            return new AzureMonitorOpenTelemetryLogEnricher(httpContextAccessor, failureMetricHandler);
                        });
                    });
            }
        }

        /// <summary>
        /// Adds the telemetry provider.
        /// </summary>
        private void AddTelemetryProvider(IServiceCollection services)
        {
            var configuration = new TelemetryConfiguration();
            Configuration.GetSection("Telemetry").Bind(configuration);
            services.AddTransient<IMeterFactory, DummyMeterFactory>();

            switch (configuration.Provider)
            {
                case TelemetryProvider.ApplicationInsights:
                    Startup.AddApplicationInsightsTelemetry(services, configuration);
                    break;
                case TelemetryProvider.OpenTelemetry:
                    Startup.AddAzureMonitorOpenTelemetry(services, configuration);
                    break;
            }
        }
    }
}
