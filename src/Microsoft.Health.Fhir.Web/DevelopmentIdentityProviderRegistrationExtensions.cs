// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using EnsureThat;
using IdentityServer4.Models;
using IdentityServer4.Test;
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
            EnsureArg.IsNotNull(services, nameof(services));
            EnsureArg.IsNotNull(configuration, nameof(configuration));

            var identityProviderConfiguration = new DevelopmentIdentityProviderConfiguration();
            configuration.GetSection("DevelopmentIdentityProvider").Bind(identityProviderConfiguration);

            services.AddIdentityServer()
                .AddDeveloperSigningCredential()
                .AddInMemoryApiResources(new List<ApiResource>
                {
                    new ApiResource(
                        identityProviderConfiguration.Audience,
                        claimTypes: new List<string>() { ClaimTypes.Role, ClaimTypes.Name, ClaimTypes.NameIdentifier, ClaimTypes.Email }),
                })
                .AddTestUsers(GetTestUsers(identityProviderConfiguration.Users))
                .AddInMemoryClients(new List<Client>
                {
                    new Client
                    {
                        ClientId = identityProviderConfiguration.ClientId,

                        // Use ether client credentials or password for authentication
                        AllowedGrantTypes = GrantTypes.ResourceOwnerPasswordAndClientCredentials,

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

        private static List<TestUser> GetTestUsers(IReadOnlyList<DevelopmentIdentityProviderUser> configUsers)
        {
            return configUsers?.Select(user => new TestUser
            {
                SubjectId = user.SubjectId,
                Username = user.UserName,
                Password = user.Password,
                Claims = user.Roles.Select(r => new Claim(ClaimTypes.Role, r)).ToList(),
            }).ToList();
        }

        /// <summary>
        /// Adds the in-process identity provider to the pipeline.
        /// </summary>
        /// <param name="app">The application builder</param>
        /// <returns>The application builder.</returns>
        public static IApplicationBuilder UseDevelopmentIdentityProvider(this IApplicationBuilder app)
        {
            EnsureArg.IsNotNull(app, nameof(app));

            return app.UseIdentityServer();
        }
    }
}
