// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using IdentityServer4.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Health.Fhir.Web
{
    public static class DevelopmentIdentityProviderRegistrationExtensions
    {
        /// <summary>
        /// Adds an in-process identity provider.
        /// </summary>
        /// <param name="services">The services collection.</param>
        /// <param name="configuration">The configuration root. The "DevelopmentIdentityProvider" section will be used to populate configuration values.</param>
        /// <returns>The same services collection.</returns>
        public static IServiceCollection AddDevelopmentIdentityProvider(this IServiceCollection services, IConfiguration configuration)
        {
            var identityProviderConfiguration = new DevelopmentIdentityProviderConfiguration();
            configuration.GetSection("DevelopmentIdentityProvider").Bind(identityProviderConfiguration);

            services.AddIdentityServer()
                .AddDeveloperSigningCredential()
                .AddInMemoryApiResources(new List<ApiResource>
                {
                    new ApiResource(identityProviderConfiguration.Audience),
                })
                .AddInMemoryClients(new List<Client>
                {
                    new Client
                    {
                        ClientId = identityProviderConfiguration.ClientId,

                        // no interactive user, use the clientid/secret for authentication
                        AllowedGrantTypes = GrantTypes.ClientCredentials,

                        // secret for authentication
                        ClientSecrets =
                        {
                            new Secret(identityProviderConfiguration.ClientSecret.Sha256()),
                        },

                        // scopes that client has access to
                        AllowedScopes = { identityProviderConfiguration.Audience },
                    },
                });

            return services;
        }

        /// <summary>
        /// Adds the in-process identity provider to the pipeline.
        /// </summary>
        /// <param name="app">The application builder</param>
        /// <returns>The application builder.</returns>
        public static IApplicationBuilder UseDevelopmentIdentityProvider(this IApplicationBuilder app) => app.UseIdentityServer();
    }
}
