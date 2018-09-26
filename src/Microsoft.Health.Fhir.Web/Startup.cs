// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using IdentityServer4.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Web.Features.IdentityServer;

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
            services
                .AddFhirServer(Configuration)
                .AddCosmosDb();

            var identityServerConfiguration = new IdentityServerConfiguration();
            Configuration.GetSection("IdentityServer").Bind(identityServerConfiguration);
            services.AddIdentityServer()
                .AddDeveloperSigningCredential()
                .AddInMemoryApiResources(new List<ApiResource>
                {
                    new ApiResource(identityServerConfiguration.Audience),
                })
                .AddInMemoryClients(new List<Client>
                {
                    new Client
                    {
                        ClientId = identityServerConfiguration.ClientId,

                        // no interactive user, use the clientid/secret for authentication
                        AllowedGrantTypes = GrantTypes.ClientCredentials,

                        // secret for authentication
                        ClientSecrets =
                        {
                            new Secret(identityServerConfiguration.ClientSecret.Sha256()),
                        },

                        // scopes that client has access to
                        AllowedScopes = { identityServerConfiguration.Audience },
                    },
                });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public virtual void Configure(IApplicationBuilder app)
        {
            app.UseFhirServer();

            app.UseIdentityServer();
        }
    }
}
