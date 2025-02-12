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
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Models;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Validation.AspNetCore;

namespace Microsoft.Health.Fhir.Web
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "Contains extension methods.")]
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

                services.AddDbContext<ApplicationAuthDbContext>(options =>
                {
                    options.UseInMemoryDatabase("DevAuthDb");
                    options.UseOpenIddict();
                });

                services.AddOpenIddict()
                    .AddCore(options =>
                    {
                        // Register the in-memory stores.
                        options.UseEntityFrameworkCore()
                            .UseDbContext<ApplicationAuthDbContext>();
                    })
                    .AddServer(options =>
                    {
                        // Minimal token endpoint
                        options.SetTokenEndpointUris("/connect/token");

                        // Dev flows:
                        options.AllowClientCredentialsFlow();
                        options.AllowPasswordFlow();

                        // Development signing - no persistent store required
                        options.AddDevelopmentSigningCertificate();
                        options.AddDevelopmentEncryptionCertificate();
                        options.DisableAccessTokenEncryption();

                        // For testing, you can allow clients without secrets:
                        // options.AcceptAnonymousClients();

                        // ASP.NET Core integration
                        options.UseAspNetCore()
                            .EnableTokenEndpointPassthrough();

                        // Register sample scope strings (replace usage of ApiScope).
                        options.RegisterScopes(
                            "fhirUser",
                            DevelopmentIdentityProviderConfiguration.Audience,
                            WrongAudienceClient);
                        options.RegisterScopes(smartScopes.ToArray());

                        options.AddEventHandler<OpenIddictServerEvents.ValidateTokenRequestContext>(builder =>
                            builder.UseInlineHandler(context =>
                            {
                                DevelopmentIdentityProviderApplicationConfiguration client = developmentIdentityProviderConfiguration
                                    .ClientApplications
                                    .FirstOrDefault(x => x.Id == context.ClientId);

                                if (client == null)
                                {
                                    context.Reject(
                                        error: OpenIddictConstants.Errors.InvalidClient,
                                        description: "The specified 'client_id' doesn't match a registered application.");
                                }

                                if (!string.Equals(client.Id, context.ClientSecret, StringComparison.Ordinal))
                                {
                                    context.Reject(
                                        error: OpenIddictConstants.Errors.InvalidGrant,
                                        description: "The specified 'client_secret' is invalid.");
                                }

                                return default;
                            }));
                    });

                services.AddAuthentication(options =>
                {
                    options.DefaultScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
                });
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
            var devIdpConfig = app.ApplicationServices.GetService<IOptions<DevelopmentIdentityProviderConfiguration>>();

            if (devIdpConfig?.Value?.Enabled == true)
            {
                app.UseAuthentication();
                app.UseAuthorization();
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
        /// <param name="existingConfiguration">The existing configuration.</param>
        /// <returns>The same configuration builder.</returns>
        public static IConfigurationBuilder AddDevelopmentAuthEnvironmentIfConfigured(this IConfigurationBuilder configurationBuilder, IConfigurationRoot existingConfiguration)
        {
            EnsureArg.IsNotNull(existingConfiguration, nameof(existingConfiguration));

            string testEnvironmentFilePath = existingConfiguration["TestAuthEnvironment:FilePath"];
            testEnvironmentFilePath = Path.GetFullPath(testEnvironmentFilePath);
            if (string.IsNullOrWhiteSpace(testEnvironmentFilePath))
            {
                return configurationBuilder;
            }

            if (!File.Exists(testEnvironmentFilePath))
            {
                return configurationBuilder;
            }

            return configurationBuilder.Add(new DevelopmentAuthEnvironmentConfigurationSource(testEnvironmentFilePath, existingConfiguration));
        }

        private static List<string> GenerateSmartClinicalScopes()
        {
            ModelExtensions.SetModelInfoProvider();
            var resourceTypes = ModelInfoProvider.Instance.GetResourceTypeNames();
            var scopes = new List<string>();

            scopes.Add("patient/*.*");
            scopes.Add("user/*.*");
            scopes.Add("system/*.*");
            scopes.Add("system/*.read");
            scopes.Add("patient/*.read");
            scopes.Add("user/*.write");
            scopes.Add("user/*.read");

            foreach (var resourceType in resourceTypes)
            {
                scopes.Add($"patient/{resourceType}.*");
                scopes.Add($"user/{resourceType}.*");
                scopes.Add($"patient/{resourceType}.read");
                scopes.Add($"user/{resourceType}.read");
                scopes.Add($"patient/{resourceType}.write");
                scopes.Add($"user/{resourceType}.write");
                scopes.Add($"system/{resourceType}.write");
                scopes.Add($"system/{resourceType}.read");
            }

            return scopes;
        }

        private static Claim[] CreateFhirUserClaims(string userId, string host)
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

            return new[]
            {
                new Claim("appid", userId),
                new Claim("fhirUser", $"{host}{userType}/" + userId),
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
                private static readonly Dictionary<string, string> Mappings = new Dictionary<string, string>
                {
                    { "^users:", "DevelopmentIdentityProvider:Users:" },
                    { "^clientApplications:", "DevelopmentIdentityProvider:ClientApplications:" },
                };

                private readonly IConfigurationRoot _existingConfiguration;

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
                    if (string.IsNullOrWhiteSpace(_existingConfiguration[AudienceKey]))
                    {
                        Data[AudienceKey] = DevelopmentIdentityProviderConfiguration.Audience;
                    }

                    Data[DevelopmentIdpEnabledKey] = bool.TrueString;

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
