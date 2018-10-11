// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using EnsureThat;
using IdentityServer4.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.Health.Fhir.Web
{
    public static class DevelopmentIdentityProviderRegistrationExtensions
    {
        /// <summary>
        /// Adds an in-process identity provider if enabled in configuration.
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
            services.AddSingleton(Options.Create(identityProviderConfiguration));

            if (identityProviderConfiguration.Enabled)
            {
                services.AddIdentityServer()
                    .AddDeveloperSigningCredential()
                    .AddInMemoryApiResources(new List<ApiResource>
                    {
                        new ApiResource(DevelopmentIdentityProviderConfiguration.Audience),
                    })
                    .AddInMemoryClients(
                        identityProviderConfiguration.ClientApplications.Select(
                            applicationConfiguration =>
                                new Client
                                {
                                    ClientId = applicationConfiguration.Id,

                                    // no interactive user, use the clientid/secret for authentication
                                    AllowedGrantTypes = GrantTypes.ClientCredentials,

                                    // secret for authentication
                                    ClientSecrets = { new Secret(applicationConfiguration.Id.Sha256()) },

                                    // scopes that client has access to
                                    AllowedScopes = { DevelopmentIdentityProviderConfiguration.Audience },

                                    // app roles that the client app may have
                                    Claims = applicationConfiguration.Roles.Select(r => new Claim(ClaimTypes.Role, r)).ToList(),
                                }));
            }

            return services;
        }

        /// <summary>
        /// Adds the in-process identity provider to the pipeline if enabled in configuration.
        /// </summary>
        /// <param name="app">The application builder</param>
        /// <returns>The application builder.</returns>
        public static IApplicationBuilder UseDevelopmentIdentityProvider(this IApplicationBuilder app)
        {
            EnsureArg.IsNotNull(app, nameof(app));
            if (app.ApplicationServices.GetService<IOptions<DevelopmentIdentityProviderConfiguration>>()?.Value?.Enabled == true)
            {
                app.UseIdentityServer();
            }

            return app;
        }
    }
}
