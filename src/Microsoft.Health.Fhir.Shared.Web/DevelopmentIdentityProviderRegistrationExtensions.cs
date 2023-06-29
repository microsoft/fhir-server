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
using AngleSharp.Common;
using EnsureThat;
using IdentityServer4.Models;
using IdentityServer4.Test;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Web
{
    public static class DevelopmentIdentityProviderRegistrationExtensions
    {
        private const string WrongAudienceClient = "wrongAudienceClient";

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

            var smartScopes = GenerateSmartClinicalScopes();

            if (developmentIdentityProviderConfiguration.Enabled)
            {
                var host = configuration["ASPNETCORE_URLS"];

                services.AddIdentityServer()
                    .AddDeveloperSigningCredential()
                    .AddInMemoryApiScopes(new[]
                    {
                        new ApiScope(DevelopmentIdentityProviderConfiguration.Audience),
                        new ApiScope(WrongAudienceClient),
                        new ApiScope("fhirUser"),
                    }.Concat(smartScopes))
                    .AddInMemoryApiResources(new[]
                    {
                        new ApiResource(
                            DevelopmentIdentityProviderConfiguration.Audience,
                            userClaims: new[] { authorizationConfiguration.RolesClaim, "fhirUser" })
                        {
                            Scopes = new[] { DevelopmentIdentityProviderConfiguration.Audience, "fhirUser" }.Concat(smartScopes.Select(s => s.Name)).ToList(),
                        },
                        new ApiResource(
                            WrongAudienceClient,
                            userClaims: new[] { authorizationConfiguration.RolesClaim })
                        {
                            Scopes = { WrongAudienceClient },
                        },
                    })
                    .AddTestUsers(developmentIdentityProviderConfiguration.Users?.Select((user) =>
                        {
                            var userClaims = user.Roles.Select(r => new Claim(authorizationConfiguration.RolesClaim, r)).ToList();
                            userClaims.Add(new Claim("fhirUser", host + "Patient/" + user.Id));
                            return new TestUser
                            {
                                Username = user.Id,
                                Password = user.Id,
                                IsActive = true,
                                SubjectId = user.Id,
                                Claims = userClaims,
                            };
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
                                    AllowedScopes = new[] { DevelopmentIdentityProviderConfiguration.Audience, WrongAudienceClient, "fhirUser" }.Concat(smartScopes.Select(s => s.Name)).ToList(),

                                    // app roles that the client app may have
                                    Claims = applicationConfiguration.Roles.Select(
                                        r => new ClientClaim(authorizationConfiguration.RolesClaim, r))
                                            .Concat(CreateFhirUserClaims(applicationConfiguration.Id, host))
                                            .ToList(),

                                    ClientClaimsPrefix = string.Empty,
                                }))
                ;
            }

            return services;
        }

        /// <summary>
        /// Adds the in-process identity provider to the pipeline if enabled in configuration.
        /// </summary>
        /// <param name="app">The application builder</param>
        /// <returns>The application builder.</returns>
        public static IApplicationBuilder UseDevelopmentIdentityProviderIfConfigured(this IApplicationBuilder app)
        {
            EnsureArg.IsNotNull(app, nameof(app));
            if (app.ApplicationServices.GetService<IOptions<DevelopmentIdentityProviderConfiguration>>()?.Value?.Enabled == true)
            {
                app.UseIdentityServer();
            }

            return app;
        }

        /// <summary>
        /// If <paramref name="existingConfiguration"/> contains a value for TestAuthEnvironment:FilePath and the file exists, this method adds an <see cref="IConfigurationBuilder"/> that
        /// reads a testauthenvironment.json file and reshapes it to fit into the expected schema of the FHIR server
        /// configuration. Also sets the security audience and sets the development identity server as enabled.
        /// This is an optional configuration source and is only intended to be used for local development.
        /// </summary>
        /// <param name="configurationBuilder">The configuration builder.</param>
        /// <param name="existingConfiguration">abc</param>
        /// <returns>The same configuration builder.</returns>
        public static IConfigurationBuilder AddDevelopmentAuthEnvironmentIfConfigured(this IConfigurationBuilder configurationBuilder, IConfigurationRoot existingConfiguration)
        {
            EnsureArg.IsNotNull(existingConfiguration, nameof(existingConfiguration));

            string testEnvironmentFilePath = existingConfiguration["TestAuthEnvironment:FilePath"];

            if (string.IsNullOrWhiteSpace(testEnvironmentFilePath))
            {
                return configurationBuilder;
            }

            testEnvironmentFilePath = Path.GetFullPath(testEnvironmentFilePath);
            if (!File.Exists(testEnvironmentFilePath))
            {
                return configurationBuilder;
            }

            return configurationBuilder.Add(new DevelopmentAuthEnvironmentConfigurationSource(testEnvironmentFilePath, existingConfiguration));
        }

        private static IReadOnlyCollection<ApiScope> GenerateSmartClinicalScopes()
        {
            ModelExtensions.SetModelInfoProvider();
            var resourceTypes = ModelInfoProvider.Instance.GetResourceTypeNames();
            var scopes = new List<ApiScope>();

            scopes.Add(new ApiScope("patient/*.*"));
            scopes.Add(new ApiScope("user/*.*"));
            scopes.Add(new ApiScope("system/*.*"));
            scopes.Add(new ApiScope("patient/*.read"));
            scopes.Add(new ApiScope("user/*.write"));
            scopes.Add(new ApiScope("user/*.read"));

            foreach (var resourceType in resourceTypes)
            {
                scopes.Add(new ApiScope($"patient/{resourceType}.*"));
                scopes.Add(new ApiScope($"user/{resourceType}.*"));
                scopes.Add(new ApiScope($"patient/{resourceType}.read"));
                scopes.Add(new ApiScope($"user/{resourceType}.read"));
                scopes.Add(new ApiScope($"patient/{resourceType}.write"));
                scopes.Add(new ApiScope($"user/{resourceType}.write"));
                scopes.Add(new ApiScope($"system/{resourceType}.write"));
                scopes.Add(new ApiScope($"system/{resourceType}.read"));
            }

            return scopes;
        }

        private static IEnumerable<ClientClaim> CreateFhirUserClaims(string userId, string host)
        {
            string userType = null;

            if (userId.Contains("patient", StringComparison.OrdinalIgnoreCase))
            {
                userType = "Patient";
            }
            else if (userId.Contains("practitioner", StringComparison.OrdinalIgnoreCase))
            {
                userType = "Practitioner";
            }
            else if (userId.Contains("system", StringComparison.OrdinalIgnoreCase))
            {
                userType = "System";
            }

            return new ClientClaim[]
            {
                new ClientClaim("appid", userId),
                new ClientClaim("fhirUser", $"{host}{userType}/" + userId),
            };
        }

        private sealed class DevelopmentAuthEnvironmentConfigurationSource : IConfigurationSource
        {
            private readonly string _filePath;
            private readonly IConfigurationRoot _existingConfiguration;

            public DevelopmentAuthEnvironmentConfigurationSource(string filePath, IConfigurationRoot existingConfiguration)
            {
                EnsureArg.IsNotNullOrWhiteSpace(filePath, nameof(filePath));
                _filePath = filePath;
                _existingConfiguration = existingConfiguration;
            }

            public IConfigurationProvider Build(IConfigurationBuilder builder)
            {
                var jsonConfigurationSource = new JsonConfigurationSource
                {
                    Path = _filePath,
                    Optional = true,
                };

                jsonConfigurationSource.ResolveFileProvider();
                return new Provider(jsonConfigurationSource, _existingConfiguration);
            }

            private sealed class Provider : JsonConfigurationProvider
            {
                private const string AuthorityKey = "FhirServer:Security:Authentication:Authority";
                private const string AudienceKey = "FhirServer:Security:Authentication:Audience";
                private const string DevelopmentIdpEnabledKey = "DevelopmentIdentityProvider:Enabled";
                private const string PrincipalClaimsKey = "FhirServer:Security:PrincipalClaims";

                private readonly IConfigurationRoot _existingConfiguration;

                private static readonly Dictionary<string, string> Mappings = new Dictionary<string, string>
                {
                    { "^users:", "DevelopmentIdentityProvider:Users:" },
                    { "^clientApplications:", "DevelopmentIdentityProvider:ClientApplications:" },
                };

                public Provider(JsonConfigurationSource source, IConfigurationRoot existingConfiguration)
                    : base(source)
                {
                    _existingConfiguration = existingConfiguration;
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
                    Data[DevelopmentIdpEnabledKey] = bool.TrueString;

                    if (string.IsNullOrWhiteSpace(_existingConfiguration[AudienceKey]))
                    {
                        Data[AudienceKey] = DevelopmentIdentityProviderConfiguration.Audience;
                    }

                    if (string.IsNullOrWhiteSpace(_existingConfiguration[AuthorityKey]))
                    {
                        Data[AuthorityKey] = GetAuthority();
                    }

                    Data[$"{PrincipalClaimsKey}:0"] = DevelopmentIdentityProviderConfiguration.LastModifiedClaim;
                    Data[$"{PrincipalClaimsKey}:1"] = DevelopmentIdentityProviderConfiguration.ClientIdClaim;
                }

                private string GetAuthority()
                {
                    return _existingConfiguration["ASPNETCORE_URLS"]
                        ?.Split(';', StringSplitOptions.RemoveEmptyEntries)
                        .Where(u =>
                        {
                            if (Uri.TryCreate(u, UriKind.Absolute, out Uri uri))
                            {
                                if (uri.Scheme == "https")
                                {
                                    return true;
                                }
                            }

                            return false;
                        }).FirstOrDefault()?.TrimEnd('/');
                }
            }
        }
    }
}
