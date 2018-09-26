// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Features.Registration;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Features.Initialization;

namespace Microsoft.Health.Fhir.Api.Modules
{
    /// <summary>
    /// Starts all <see cref="IStartable"/> instances in the IoC container and ensures that all <see cref="IRequireInitializationOnFirstRequest"/> instances
    /// are initialized before any controllers are invoked.
    /// </summary>
    public class InitializationModule : IStartupModule, IStartupConfiguration
    {
        /// <inheritdoc />
        public void Load(IServiceCollection services)
        {
            services.AddSingleton<IStartupConfiguration>(this);
        }

        /// <inheritdoc />
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, IApplicationLifetime appLifetime, ILoggerFactory loggerFactory)
        {
            ILogger logger = loggerFactory.CreateLogger<InitializationModule>();

            // start IStartable services.
            foreach (var startable in app.ApplicationServices.GetService<IEnumerable<IStartable>>())
            {
                using (logger.BeginTimedScope($"Initializing {startable.GetType().Name}."))
                {
                    startable.Start();
                }
            }

            // If there are any IRequireInitializationOnFirstRequest services, ensure they are initialized on the first request.

            IRequireInitializationOnFirstRequest[] requireInitializationsOnFirstRequest = app.ApplicationServices.GetService<IEnumerable<IRequireInitializationOnFirstRequest>>().ToArray();
            if (requireInitializationsOnFirstRequest.Length == 0)
            {
                return;
            }

            // Register a middleware component that will be called on every request,
            // ensuring that all components are initialized before a controller
            // handles the request.

            bool initializationComplete = false;
            app.Use(async (httpContext, next) =>
            {
                if (!initializationComplete)
                {
                    foreach (var initializable in requireInitializationsOnFirstRequest)
                    {
                        await initializable.EnsureInitialized();
                    }

                    initializationComplete = true;
                }

                await next();
            });
        }
    }
}
