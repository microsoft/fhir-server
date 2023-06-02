// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Reflection;
using EnsureThat;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Api.Features.Headers;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.ApiNotifications;
using Microsoft.Health.Fhir.Api.Features.Context;
using Microsoft.Health.Fhir.Api.Features.ExceptionNotifications;
using Microsoft.Health.Fhir.Api.Features.Exceptions;
using Microsoft.Health.Fhir.Api.Features.Operations.Export;
using Microsoft.Health.Fhir.Api.Features.Operations.Import;
using Microsoft.Health.Fhir.Api.Features.Operations.Reindex;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Api.Features.Throttling;
using Microsoft.Health.Fhir.Core.Features.Cors;
using Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration;
using Microsoft.Health.Fhir.Core.Registration;
using Polly;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class FhirServerServiceCollectionExtensions
    {
        private const string FhirServerConfigurationSectionName = "FhirServer";

        /// <summary>
        /// Adds services for enabling a FHIR server.
        /// </summary>
        /// <param name="services">The services collection.</param>
        /// <param name="configurationRoot">An optional configuration root object. This method uses "FhirServer" section.</param>
        /// <param name="configureAction">An optional delegate to set <see cref="FhirServerConfiguration"/> properties after values have been loaded from configuration</param>
        /// <returns>A <see cref="IFhirServerBuilder"/> object.</returns>
        public static IFhirServerBuilder AddFhirServer(
            this IServiceCollection services,
            IConfiguration configurationRoot = null,
            Action<FhirServerConfiguration> configureAction = null)
        {
            EnsureArg.IsNotNull(services, nameof(services));

            services.AddOptions();
            services.AddMvc(options =>
                {
                    options.EnableEndpointRouting = false;
                    options.RespectBrowserAcceptHeader = true;
                })
                .AddNewtonsoftJson(options =>
                {
                    options.SerializerSettings.DateParseHandling = Newtonsoft.Json.DateParseHandling.DateTimeOffset;
                })
                .AddRazorRuntimeCompilation();

            var fhirServerConfiguration = new FhirServerConfiguration();

            string dataStore = configurationRoot == null ? string.Empty : configurationRoot["DataStore"];
            configurationRoot?.GetSection(FhirServerConfigurationSectionName).Bind(fhirServerConfiguration);
            configureAction?.Invoke(fhirServerConfiguration);

            services.AddSingleton(Options.Options.Create(fhirServerConfiguration));
            services.AddSingleton(Options.Options.Create(fhirServerConfiguration.Security));
            services.AddSingleton(Options.Options.Create(fhirServerConfiguration.Features));
            services.AddSingleton(Options.Options.Create(fhirServerConfiguration.CoreFeatures));
            services.AddSingleton(Options.Options.Create(fhirServerConfiguration.Cors));
            services.AddSingleton(Options.Options.Create(fhirServerConfiguration.Operations));
            services.AddSingleton(Options.Options.Create(fhirServerConfiguration.Operations.Export));
            services.AddSingleton(Options.Options.Create(fhirServerConfiguration.Operations.Reindex));
            services.AddSingleton(Options.Options.Create(fhirServerConfiguration.Operations.ConvertData));
            services.AddSingleton(Options.Options.Create(fhirServerConfiguration.Operations.IntegrationDataStore));
            services.AddSingleton(Options.Options.Create(fhirServerConfiguration.Operations.Import));
            services.AddSingleton(Options.Options.Create(fhirServerConfiguration.Audit));
            services.AddSingleton(Options.Options.Create(fhirServerConfiguration.Bundle));
            services.AddSingleton(provider =>
            {
                var throttlingOptions = Options.Options.Create(fhirServerConfiguration.Throttling);
                throttlingOptions.Value.DataStore = dataStore;
                return throttlingOptions;
            });

            services.AddSingleton(Options.Options.Create(fhirServerConfiguration.ArtifactStore));
            services.AddSingleton(Options.Options.Create(fhirServerConfiguration.ImplementationGuides));
            services.AddTransient<IStartupFilter, FhirServerStartupFilter>();

            services.RegisterAssemblyModules(Assembly.GetExecutingAssembly(), fhirServerConfiguration);

            services.AddFhirServerBase(fhirServerConfiguration);

            services.AddHttpClient(Options.Options.DefaultName)
                .AddTransientHttpErrorPolicy(builder =>
                    builder.OrResult(m => m.StatusCode == HttpStatusCode.TooManyRequests)
                        .WaitAndRetryAsync(5, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))));

            var multipleRegisteredServices = services
                .GroupBy(x => (x.ServiceType, x.ImplementationType, x.ImplementationInstance, x.ImplementationFactory))
                .Where(x => x.Count() > 1)
                .ToArray();

            if (multipleRegisteredServices.Any())
            {
                foreach (var service in multipleRegisteredServices)
                {
                    Debug.WriteLine($"** IoC Config Warning: Service implementation '{service.Key.ImplementationType ?? service.Key.ImplementationInstance ?? service.Key.ImplementationFactory}' was registered multiple times.");
                }
            }

            return new FhirServerBuilder(services);
        }

        /// <summary>
        /// Adds background worker services.
        /// </summary>
        /// <param name="fhirServerBuilder">The FHIR server builder.</param>
        /// <param name="addExportWorker">Whether to add the background worker for export jobs</param>
        /// <returns>The builder.</returns>
        public static IFhirServerBuilder AddBackgroundWorkers(
            this IFhirServerBuilder fhirServerBuilder,
            bool addExportWorker)
        {
            EnsureArg.IsNotNull(fhirServerBuilder, nameof(fhirServerBuilder));

            if (addExportWorker)
            {
                fhirServerBuilder.Services.AddHostedService<ExportJobWorkerBackgroundService>();
            }

            fhirServerBuilder.Services.AddHostedService<ReindexJobWorkerBackgroundService>();

            return fhirServerBuilder;
        }

        public static IFhirServerBuilder AddBundleOrchestrator(
            this IFhirServerBuilder fhirServerBuilder,
            IConfiguration configuration)
        {
            EnsureArg.IsNotNull(fhirServerBuilder, nameof(fhirServerBuilder));
            EnsureArg.IsNotNull(configuration, nameof(configuration));

            fhirServerBuilder.Services.AddSingleton<IBundleOrchestrator, BundleOrchestrator>();

            return fhirServerBuilder;
        }

        private class FhirServerBuilder : IFhirServerBuilder
        {
            public FhirServerBuilder(IServiceCollection services)
            {
                EnsureArg.IsNotNull(services, nameof(services));
                Services = services;
            }

            public IServiceCollection Services { get; }
        }

        /// <summary>
        /// An <see cref="IStartupFilter"/> that configures middleware components before any components are added in Startup.Configure
        /// </summary>
        private class FhirServerStartupFilter : IStartupFilter
        {
            public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
            {
                return app =>
                {
                    IWebHostEnvironment env = app.ApplicationServices.GetRequiredService<IWebHostEnvironment>();

                    // This middleware will add delegates to the OnStarting method of httpContext.Response for setting headers.
                    app.UseBaseHeaders();

                    app.UseCors(Constants.DefaultCorsPolicy);

                    // This middleware should be registered at the beginning since it generates correlation id among other things,
                    // which will be used in other middlewares.
                    app.UseFhirRequestContext();

                    // Adding the notification here makes sure that we are publishing all the unknown/unhandled errors e.g 500 errors should be logged in RequestMetric
                    app.UseApiNotifications();

                    if (env.IsDevelopment())
                    {
                        app.UseDeveloperExceptionPage();
                    }
                    else
                    {
                        // This middleware will capture issues within other middleware that prevent the ExceptionHandler from completing.
                        // This should be the first middleware added because they execute in order.
                        app.UseBaseException();

                        // This middleware will capture any unhandled exceptions and attempt to return an operation outcome using the customError page
                        app.UseExceptionHandler(KnownRoutes.CustomError);

                        // This middleware will capture any handled error with the status code between 400 and 599 that hasn't had a body or content-type set. (i.e. 404 on unknown routes)
                        app.UseStatusCodePagesWithReExecute(KnownRoutes.CustomError, "?statusCode={0}");

                        // This middleware creates notifications for each exception that is encountered with the context of the current request.
                        app.UseExceptionNotificationMiddleware();
                    }

                    // The audit module needs to come after the exception handler because we need to catch the response before it gets converted to custom error.
                    app.UseAudit();

                    app.UseFhirRequestContextAuthentication();

                    app.UseMiddleware<SearchPostReroutingMiddleware>();

                    app.UseInitialImportLock();

                    // Throttling needs to come after Audit and ApiNotifications so we can audit it and track it for API metrics.
                    // It should also be after authentication
                    app.UseThrottling();

                    next(app);
                };
            }
        }
    }
}
