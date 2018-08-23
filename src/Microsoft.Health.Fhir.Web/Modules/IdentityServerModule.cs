// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using IdentityServer4.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Features.Container;
using Microsoft.Health.Fhir.Web.Features.IdentityServer;

namespace Microsoft.Health.Fhir.Web.Modules
{
    public class IdentityServerModule : IStartupModule, IStartupConfiguration
    {
        private readonly IConfiguration _configuration;
        private readonly IdentityServerConfiguration _identityServerConfiguration = new IdentityServerConfiguration();

        public IdentityServerModule(IConfiguration configuration)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));

            _configuration = configuration;
        }

        /// <inheritdoc />
        public void Load(IServiceCollection services)
        {
            EnsureArg.IsNotNull(services, nameof(services));

            _configuration.GetSection("IdentityServer").Bind(_identityServerConfiguration);
            services.AddSingleton(Options.Create(_identityServerConfiguration));

            if (_identityServerConfiguration.Enabled)
            {
                services.AddIdentityServer()
                    .AddDeveloperSigningCredential()
                    .AddInMemoryApiResources(new List<ApiResource>
                    {
                        new ApiResource(_identityServerConfiguration.Audience),
                    })
                    .AddInMemoryClients(new List<Client>
                    {
                        new Client
                        {
                            ClientId = _identityServerConfiguration.ClientId,

                            // no interactive user, use the clientid/secret for authentication
                            AllowedGrantTypes = GrantTypes.ClientCredentials,

                            // secret for authentication
                            ClientSecrets =
                            {
                                new Secret(_identityServerConfiguration.ClientSecret.Sha256()),
                            },

                            // scopes that client has access to
                            AllowedScopes = { _identityServerConfiguration.Audience },
                        },
                    });

                services.AddSingleton<IStartupConfiguration>(this);
            }
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env, IApplicationLifetime appLifetime, ILoggerFactory loggerFactory, IConfiguration configuration)
        {
            if (_identityServerConfiguration.Enabled)
            {
                app.UseIdentityServer();
            }
        }
    }
}
