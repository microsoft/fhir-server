// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using EnsureThat;
using IdentityServer4.Models;
using IdentityServer4.Test;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;

namespace Microsoft.Health.Fhir.Web
{
    public static class DevelopmentIdentityProviderRegistrationExtensions
    {
        private const string WrongAudienceClient = "wrongaudienceclient";

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

            var authorizationConfiguration = new AuthorizationConfiguration();
            configuration.GetSection("FhirServer:Security:Authorization").Bind(authorizationConfiguration);

            var developmentIdentityProviderConfiguration = new DevelopmentIdentityProviderConfiguration();
            configuration.GetSection("DevelopmentIdentityProvider").Bind(developmentIdentityProviderConfiguration);
            services.AddSingleton(Options.Create(developmentIdentityProviderConfiguration));

            if (developmentIdentityProviderConfiguration.Enabled)
            {
                services.AddIdentityServer()
                    .AddDeveloperSigningCredential()
                    .AddInMemoryApiResources(new[]
                    {
                        new ApiResource(
                        DevelopmentIdentityProviderConfiguration.Audience,
                        claimTypes: new List<string>() { authorizationConfiguration.RolesClaim, ClaimTypes.Name, ClaimTypes.NameIdentifier })
                        {
                            UserClaims = { authorizationConfiguration.RolesClaim },
                        },
                        new ApiResource(
                        WrongAudienceClient,
                        claimTypes: new List<string>() { authorizationConfiguration.RolesClaim, ClaimTypes.Name, ClaimTypes.NameIdentifier })
                        {
                            UserClaims = { authorizationConfiguration.RolesClaim },
                        },
                    })
                    .AddTestUsers(developmentIdentityProviderConfiguration.Users?.Select(user =>
                        new TestUser
                        {
                            Username = user.Id,
                            Password = user.Id,
                            IsActive = true,
                            SubjectId = user.Id,
                            Claims = user.Roles.Select(r => new Claim(authorizationConfiguration.RolesClaim, r)).ToList(),
                        }).ToList())
                    .AddInMemoryClients(
                        developmentIdentityProviderConfiguration.ClientApplications.Select(
                            applicationConfiguration =>
                                new Client
                                {
                                    ClientId = applicationConfiguration.Id,

                                    // client credentials and ROPC for testing
                                    AllowedGrantTypes = GrantTypes.ResourceOwnerPasswordAndClientCredentials,

                                    // secret for authentication
                                    ClientSecrets = { new Secret(applicationConfiguration.Id.Sha256()) },

                                    // scopes that client has access to
                                    AllowedScopes = { DevelopmentIdentityProviderConfiguration.Audience, WrongAudienceClient },

                                    // app roles that the client app may have
                                    Claims = applicationConfiguration.Roles.Select(r => new Claim(authorizationConfiguration.RolesClaim, r)).Concat(new[] { new Claim("appid", applicationConfiguration.Id) }).ToList(),

                                    ClientClaimsPrefix = string.Empty,
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

        /// <summary>
        /// If <paramref name="testEnvironmentFilePath"/> exists, adds an <see cref="IConfigurationBuilder"/> that
        /// reads a testauthenvironment.json file and reshapes it to fit into the expected schema of the FHIR server
        /// configuration. Also sets the security audience and sets the development identity server as enabled.
        /// This is an optional configuration source and is only intended to be used for local development.
        /// </summary>
        /// <param name="configurationBuilder">The configuration builder.</param>
        /// <param name="testEnvironmentFilePath">The path to the config file. Can null, empty, or non-existent.</param>
        /// <returns>The same configuration builder.</returns>
        public static IConfigurationBuilder AddDevelopmentAuthEnvironment(this IConfigurationBuilder configurationBuilder, string testEnvironmentFilePath)
        {
            if (string.IsNullOrWhiteSpace(testEnvironmentFilePath))
            {
                return configurationBuilder;
            }

            testEnvironmentFilePath = Path.GetFullPath(testEnvironmentFilePath);
            if (!File.Exists(testEnvironmentFilePath))
            {
                return configurationBuilder;
            }

            return configurationBuilder.Add(new DevelopmentAuthEnvironmentConfigurationSource(testEnvironmentFilePath));
        }

        private class DevelopmentAuthEnvironmentConfigurationSource : IConfigurationSource
        {
            private readonly string _filePath;

            public DevelopmentAuthEnvironmentConfigurationSource(string filePath)
            {
                EnsureArg.IsNotNullOrWhiteSpace(filePath, nameof(filePath));
                _filePath = filePath;
            }

            public IConfigurationProvider Build(IConfigurationBuilder builder)
            {
                var jsonConfigurationSource = new JsonConfigurationSource
                {
                    Path = _filePath,
                    Optional = true,
                };

                jsonConfigurationSource.ResolveFileProvider();
                return new Provider(jsonConfigurationSource);
            }

            private class Provider : JsonConfigurationProvider
            {
                private static readonly Dictionary<string, string> Mappings = new Dictionary<string, string>
                {
                    { "^roles:", "FhirServer:Security:Authorization:Roles:" },
                    { "^users:", "DevelopmentIdentityProvider:Users:" },
                    { "^clientApplications:", "DevelopmentIdentityProvider:ClientApplications:" },
                };

                public Provider(JsonConfigurationSource source)
                    : base(source)
                {
                }

                public override void Load()
                {
                    base.Load();

                    // remap the entries
                    Data = Data.ToDictionary(
                        p => Mappings.Aggregate(p.Key, (acc, mapping) => Regex.Replace(acc, mapping.Key, mapping.Value, RegexOptions.IgnoreCase)),
                        p => p.Value,
                        StringComparer.OrdinalIgnoreCase);

                    // add properties related to the development identity provider.
                    Data["DevelopmentIdentityProvider:Enabled"] = bool.TrueString;
                    Data["FhirServer:Security:Authentication:Audience"] = DevelopmentIdentityProviderConfiguration.Audience;
                    Data["FhirServer:Security:PrincipalClaims:0"] = DevelopmentIdentityProviderConfiguration.LastModifiedClaim;
                }
            }
        }
    }
}
