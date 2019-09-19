// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Reflection;
using EnsureThat;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Features.Context;
using Microsoft.Health.Fhir.Api.Features.Exceptions;
using Microsoft.Health.Fhir.Api.Features.Headers;
using Microsoft.Health.Fhir.Api.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Cors;
using Microsoft.Health.Fhir.Core.Registration;

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
                options.RespectBrowserAcceptHeader = true;
            });

            var fhirServerConfiguration = new FhirServerConfiguration();

            configurationRoot?.GetSection(FhirServerConfigurationSectionName).Bind(fhirServerConfiguration);
            configureAction?.Invoke(fhirServerConfiguration);

            services.AddSingleton(Options.Options.Create(fhirServerConfiguration));
            services.AddSingleton(Options.Options.Create(fhirServerConfiguration.Security));
            services.AddSingleton(Options.Options.Create(fhirServerConfiguration.Conformance));
            services.AddSingleton(Options.Options.Create(fhirServerConfiguration.Features));
            services.AddSingleton(Options.Options.Create(fhirServerConfiguration.Cors));
            services.AddSingleton(Options.Options.Create(fhirServerConfiguration.Operations));
            services.AddSingleton(Options.Options.Create(fhirServerConfiguration.Operations.Export));
            services.AddSingleton(Options.Options.Create(fhirServerConfiguration.Audit));

            services.AddTransient<IStartupFilter, FhirServerStartupFilter>();

            services.RegisterAssemblyModules(Assembly.GetExecutingAssembly(), fhirServerConfiguration);

            services.AddFhirServerBase(fhirServerConfiguration);

            services.AddHttpClient();

            return new FhirServerBuilder(services);
        }

        /// <summary>
        /// Adds the export worker background service.
        /// </summary>
        /// <param name="fhirServerBuilder">The FHIR server builder.</param>
        /// <returns>The builder.</returns>
        public static IFhirServerBuilder AddExportWorker(
            this IFhirServerBuilder fhirServerBuilder)
        {
            EnsureArg.IsNotNull(fhirServerBuilder, nameof(fhirServerBuilder));

            fhirServerBuilder.Services.AddHostedService<ExportJobWorkerBackgroundService>();

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
                    IHostingEnvironment env = app.ApplicationServices.GetRequiredService<IHostingEnvironment>();

                    // This middleware will add delegates to the OnStarting method of httpContext.Response for setting headers.
                    app.UseBaseHeaders();

                    app.UseCors(Constants.DefaultCorsPolicy);

                    // This middleware should be registered at the beginning since it generates correlation id among other things,
                    // which will be used in other middlewares.
                    app.UseFhirRequestContext();

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
                        app.UseExceptionHandler("/CustomError");

                        // This middleware will capture any handled error with the status code between 400 and 599 that hasn't had a body or content-type set. (i.e. 404 on unknown routes)
                        app.UseStatusCodePagesWithReExecute("/CustomError", "?statusCode={0}");
                    }

                    // The audit module needs to come after the exception handler because we need to catch
                    // the response before it gets converted to custom error.
                    app.UseAudit();

                    app.UseAuthentication();

                    // Now that we've authenticated the user, update the context with any post-authentication info.
                    app.UseFhirRequestContextAfterAuthentication();

                    next(app);
                };
            }
        }
    }
}
