// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using EnsureThat;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.OpenIddict.Configuration;
using Microsoft.Health.Fhir.Api.OpenIddict.Controllers;
using Microsoft.Health.Fhir.Api.OpenIddict.Data;
using Microsoft.Health.Fhir.Api.OpenIddict.Services;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Models;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Validation.AspNetCore;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Microsoft.Health.Fhir.Api.OpenIddict.Extensions
{
    public static class DevelopmentIdentityProviderRegistrationExtensions
    {
        internal const string WrongAudienceClient = "wrongAudienceClient";

        internal static readonly string[] AllowedGrantTypes = new string[]
        {
            OpenIddictConstants.GrantTypes.AuthorizationCode,
            OpenIddictConstants.GrantTypes.ClientCredentials,
            OpenIddictConstants.GrantTypes.Password,
        };

        internal static readonly string[] AllowedScopes = new string[]
        {
            DevelopmentIdentityProviderConfiguration.Audience,
            WrongAudienceClient,
            "fhirUser",
        };

        /// <summary>
        /// Adds an in-process identity provider if enabled in configuration.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configuration">The configuration root. The "DevelopmentIdentityProvider" section will be used to populate configuration values.</param>
        /// <returns>The same services collection.</returns>
        public static IServiceCollection AddDevelopmentIdentityProvider(this IServiceCollection services, IConfiguration configuration)
        {
            EnsureArg.IsNotNull(services, nameof(services));
            EnsureArg.IsNotNull(configuration, nameof(configuration));

            // The authorization configuration must be registered for OpenIddictApplicationCreater.
            var authorizationConfiguration = new AuthorizationConfiguration();
            configuration.GetSection("FhirServer:Security:Authorization").Bind(authorizationConfiguration);
            services.AddSingleton(authorizationConfiguration);

            var developmentIdentityProviderConfiguration = new DevelopmentIdentityProviderConfiguration();
            configuration.GetSection("DevelopmentIdentityProvider").Bind(developmentIdentityProviderConfiguration);
            services.AddSingleton(Options.Create(developmentIdentityProviderConfiguration));

            var smartScopes = GenerateSmartClinicalScopes();

            if (developmentIdentityProviderConfiguration.Enabled)
            {
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
                        // Note: Keep "/connect/token" at the top so OpenIddict will return the correct token endpoint
                        //   on "/.well-known/openid-configuration".
                        options.SetTokenEndpointUris(
                            "/connect/token",
                            "/AadSmartOnFhirProxy/token");
                        options.SetAuthorizationEndpointUris("/AadSmartOnFhirProxy/authorize");

                        // Dev flows:
                        options.AllowAuthorizationCodeFlow();
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
                            .EnableAuthorizationEndpointPassthrough()
                            .EnableTokenEndpointPassthrough()
                            .DisableTransportSecurityRequirement();

                        // Register sample scope strings (replace usage of ApiScope).
                        options.RegisterScopes(AllowedScopes);
                        options.RegisterScopes(smartScopes.ToArray());

                        options.RegisterClaims("fhirUser");

                        // Enable this line if we choose to disable some of the default validation handler OpenIddict uses.
                        // options.EnableDegradedMode();

                        // Custom event handlers to replace dynamic search parameters in SMART v2 scopes with wildcards.
                        options.AddEventHandler<OpenIddictServerEvents.ValidateTokenRequestContext>(builder =>
                            builder.UseInlineHandler(context =>
                            {
                                if (!string.IsNullOrEmpty(context.Request.Scope))
                                {
                                    // Store the original scope value.
                                    context.Transaction.Properties["raw_scope"] = context.Request.Scope;

                                    var originalScopes = context.Request.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                    var normalizedScopes = new List<string>();

                                    foreach (var scope in originalScopes)
                                    {
                                        if (scope.Contains('?', StringComparison.CurrentCultureIgnoreCase))
                                        {
                                            int index = scope.IndexOf('?', StringComparison.CurrentCultureIgnoreCase);

                                            // Replace the dynamic query part with a fixed wildcard.
                                            normalizedScopes.Add(string.Concat(scope.AsSpan(0, index), "?*"));
                                        }
                                        else
                                        {
                                            normalizedScopes.Add(scope);
                                        }
                                    }

                                    context.Request.Scope = string.Join(" ", normalizedScopes);
                                }

                                return default;
                            }).SetOrder(int.MinValue)); // Ensure this runs early

                        // Note: OpenIddict has a default token validation handler that does more granular validation
                        //   including checking the cliend Id and secret. So, we may not need this event handler.
                        //   https://github.com/openiddict/openiddict-core/blob/38e84b862dc4ac765ee90d673999f6dc97354815/src/OpenIddict.Server/OpenIddictServerHandlers.Exchange.cs#L24
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
                                    return default;
                                }

                                if (!string.Equals(client.Id, context.ClientSecret, StringComparison.Ordinal))
                                {
                                    context.Reject(
                                        error: OpenIddictConstants.Errors.InvalidGrant,
                                        description: "The specified 'client_secret' is invalid.");
                                }

                                return default;
                            }));

                        // Dynamically validate SMART scopes without pre-registering all 6000+
                        options.AddEventHandler<OpenIddictServerEvents.ValidateAuthorizationRequestContext>(builder =>
                            builder.UseInlineHandler(context =>
                            {
                                if (!string.IsNullOrEmpty(context.Request.Scope))
                                {
                                    var scopes = context.Request.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                    var validScopes = new HashSet<string>(AllowedScopes, StringComparer.OrdinalIgnoreCase);
                                    var smartScopePattern = GenerateSmartClinicalScopes();

                                    foreach (var scope in scopes)
                                    {
                                        if (!validScopes.Contains(scope) && !smartScopePattern.Contains(scope))
                                        {
                                            context.Reject(
                                                error: OpenIddictConstants.Errors.InvalidScope,
                                                description: $"The scope '{scope}' is not supported.");
                                            return default;
                                        }
                                    }
                                }

                                return default;
                            }));
                    });

                services.AddAuthentication(options =>
                {
                    options.DefaultScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
                });

                services.AddHostedService<OpenIddictApplicationCreater>();

                services.TryAddTransient<OpenIddictAuthorizationController>();
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

        private static IEnumerable<string> GeneratePermissionCombinations()
        {
            // Basic permission letters: create, read, update, delete, search.
            char[] ops = new[] { 'c', 'r', 'u', 'd', 's' };
            int n = ops.Length;
            var sb = new StringBuilder();

            // There are 2^n - 1 non-empty combinations.
            for (int mask = 1; mask < (1 << n); mask++)
            {
                sb.Clear();
                for (int j = 0; j < n; j++)
                {
                    if ((mask & (1 << j)) != 0)
                    {
                        sb.Append(ops[j]);
                    }
                }

                // Do not sort the combination, so the order remains as "cruds".
                yield return sb.ToString();
            }
        }

        internal static List<string> GenerateSmartClinicalScopes()
        {
            // Generating the full list of scopes for all resource types is slow and
            // consumes an excessive amount of memory. Instead, we will generate the
            // scopes for a subset of the resource types
            // var resourceTypes = ModelInfoProvider.Instance.GetResourceTypeNames();

            var resourceTypes = new[]
            {
                "AllergyIntolerance",
                "Appointment",
                "AuditEvent",
                "Bundle",
                "CarePlan",
                "Condition",
                "Device",
                "DiagnosticReport",
                "Encounter",
                "Group",
                "Immunization",
                "MedicationRequest",
                "MedicationDispense",
                "Observation",
                "Patient",
                "Practitioner",
                "Procedure",
                "Sequence",
                "ServiceRequest",
                "ValueSet",
                "StructureDefinition",
                "Specimen",
                "SearchParameter",
                "Organization",
                "Location",
                "Provenance",
                "Composition",
                "Medication",
                "MedicationAdministration",
            };

            var scopes = new List<string>();

            // Global wildcard scopes for all resources (SMART v1)
            scopes.Add("patient/*.*");
            scopes.Add("patient/*.read");
            scopes.Add("patient/*.write");
            scopes.Add("system/*.*");
            scopes.Add("system/*.read");
            scopes.Add("system/*.write");
            scopes.Add("user/*.*");
            scopes.Add("user/*.read");
            scopes.Add("user/*.write");

            foreach (var resourceType in resourceTypes)
            {
                // SMART v1 scopes
                scopes.Add($"patient/{resourceType}.*");
                scopes.Add($"patient/{resourceType}.read");
                scopes.Add($"patient/{resourceType}.write");
                scopes.Add($"system/{resourceType}.*");
                scopes.Add($"system/{resourceType}.write");
                scopes.Add($"system/{resourceType}.read");
                scopes.Add($"user/{resourceType}.*");
                scopes.Add($"user/{resourceType}.read");
                scopes.Add($"user/{resourceType}.write");

                // SMART v2 granular permission scopes for patient, user, and system contexts.
                foreach (var prefix in new[] { "patient", "user", "system" })
                {
                    foreach (var combo in GeneratePermissionCombinations())
                    {
                        scopes.Add($"{prefix}/{resourceType}.{combo}");
                    }
                }

                // SMART v2 scopes could have any dynamic search parameter
                // We would replace them with wildcard * in auth layer
                foreach (var prefix in new[] { "patient", "user", "system" })
                {
                    foreach (var combo in GeneratePermissionCombinations())
                    {
                        scopes.Add($"{prefix}/{resourceType}.{combo}?*");
                    }
                }
            }

            // SMART v2 granular permission scopes for all resource types with wildcard *
            foreach (var prefix in new[] { "patient", "user", "system" })
            {
                foreach (var combo in GeneratePermissionCombinations())
                {
                    scopes.Add($"{prefix}/*.{combo}");
                    scopes.Add($"{prefix}/*.{combo}?*");
                }
            }

            return scopes;
        }

        /// <summary>
        /// Creates a SHA256 hash of the specified input.
        /// </summary>
        /// <param name="input">The input.</param>
        /// <returns>A hash</returns>
        internal static string Sha256(this string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = SHA256.HashData(bytes);

            return Convert.ToBase64String(hash);
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
