// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.ForwardedHeaders
{
    [RequiresTrustedForwarders]
    public class StartupWithTrustedForwarders : StartupBaseForCustomProviders
    {
        public StartupWithTrustedForwarders(IConfiguration configuration)
            : base(configuration)
        {
        }

        public override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);

            // Startup filters run in registration order, so insert first to run before UseForwardedHeaders.
            services.Insert(0, ServiceDescriptor.Singleton<IStartupFilter, UntrustedRemoteIpStartupFilter>());
        }

        /// <summary>
        /// The in-proc TestServer has no remote IP address, which cannot be attested against the trusted forwarders.
        /// Real connections always have one, so simulate a caller outside the trusted networks.
        /// </summary>
        private sealed class UntrustedRemoteIpStartupFilter : IStartupFilter
        {
            private static readonly IPAddress UntrustedAddress = IPAddress.Parse("198.51.100.7");

            public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
            {
                return app =>
                {
                    app.Use((context, nextMiddleware) =>
                    {
                        context.Connection.RemoteIpAddress = UntrustedAddress;
                        return nextMiddleware();
                    });

                    next(app);
                };
            }
        }
    }
}
