// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Reflection;
using EnsureThat;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.Container;
using Microsoft.Health.Fhir.Api.Features.Context;
using Microsoft.Health.Fhir.Api.Features.Exceptions;
using Microsoft.Health.Fhir.Api.Features.Headers;
using Microsoft.Health.Fhir.Core.Configs;

namespace Microsoft.Health.Fhir.Api
{
    /// <summary>
    /// An abstract base ASP.NET Core Startup class. It is abstract to allow configuration to be customized depending
    /// on the hosting context.
    /// </summary>
    /// <typeparam name="TLoggerCategoryName">The generic type argument of the the <see cref="ILogger{TCategoryName}"/> constructor argument</typeparam>
    public abstract class StartupBase<TLoggerCategoryName>
    {
        private readonly ILogger<TLoggerCategoryName> _logger;

        protected StartupBase(IHostingEnvironment env, ILogger<TLoggerCategoryName> logger, IConfiguration configuration)
        {
            EnsureArg.IsNotNull(env, nameof(env));
            EnsureArg.IsNotNull(logger, nameof(logger));
            _logger = logger;

            _logger.LogInformation("Applications starting on {machineName}", Environment.MachineName);

            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        /// <summary>
        /// Gets the sequence of assemblies to probe for <see cref="IStartupModule" /> implementations.
        /// </summary>
        protected virtual IEnumerable<Assembly> AssembliesContainingStartupModules
        {
            get { yield return typeof(StartupBase<TLoggerCategoryName>).Assembly; }
        }

        public virtual void ConfigureServices(IServiceCollection services)
        {
            EnsureArg.IsNotNull(services, nameof(services));
            _logger.LogInformation("Configuring services on startup.");

            services.AddSingleton(Configuration);

            services
                .Configure<ConformanceConfiguration>(Configuration.GetSection("Conformance"))
                .Configure<FeatureConfiguration>(Configuration.GetSection("Features"));

            services.AddOptions();
            services.AddMvc(options =>
            {
                options.RespectBrowserAcceptHeader = true;
            });

            foreach (Assembly assembly in AssembliesContainingStartupModules)
            {
                services.RegisterAssemblyModules(assembly, Configuration);
            }
        }

        public virtual void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory, IApplicationLifetime appLifetime)
        {
            EnsureArg.IsNotNull(app, nameof(app));
            EnsureArg.IsNotNull(env, nameof(env));
            EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory));
            EnsureArg.IsNotNull(appLifetime, nameof(appLifetime));

            _logger.LogInformation("Configuring application on startup.");

            // This middleware will add delegates to the OnStarting method of httpContext.Response for setting headers.
            app.UseBaseHeaders();

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

            app.UseAuthentication();

            foreach (var module in app.ApplicationServices.GetService<IEnumerable<IStartupConfiguration>>())
            {
                module.Configure(app, env, appLifetime, loggerFactory, Configuration);
            }

            app.UseFhirRequestContext();
            app.UseStaticFiles();
            app.UseMvc();
        }
    }
}
