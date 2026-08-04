// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Features.Tenancy;
using Microsoft.Health.Fhir.Api.Registration;
using Microsoft.Health.Fhir.Core.Features.Tenancy;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Tenancy
{
    /// <summary>
    /// Hosts two independent tenant containers in-process. Each container configures a distinct JWT issuer,
    /// audience, signing key, and scoped probe service.
    /// </summary>
    public sealed class TenantIsolationTestServerFixture : IAsyncLifetime
    {
        /// <summary>
        /// The host that resolves to the alpha tenant.
        /// </summary>
        public const string AlphaHost = "alpha.example.org";

        /// <summary>
        /// The host that resolves to the beta tenant.
        /// </summary>
        public const string BetaHost = "beta.example.org";

        /// <summary>
        /// The response header added by the stateless MVC global filter.
        /// </summary>
        public const string MvcFilterHeaderName = "X-Tenant-Isolation-Global-Filter";

        /// <summary>
        /// The stateless MVC global filter's response value.
        /// </summary>
        public const string MvcFilterHeaderValue = "tenant-neutral";

        private static readonly IReadOnlyDictionary<string, string> SigningKeys =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["alpha"] = "alpha-signing-key-alpha-signing-key-alpha!!",
                ["beta"] = "beta-signing-key-beta-signing-key-beta!!!!",
            };

        private IHost _host;

        /// <summary>
        /// Gets the in-process server.
        /// </summary>
        public TestServer Server { get; private set; }

        /// <inheritdoc />
        public async Task InitializeAsync()
        {
            _host = await new HostBuilder()
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder.UseTestServer();
                    webBuilder.ConfigureServices(ConfigureServices);
                    webBuilder.Configure(Configure);
                })
                .StartAsync();

            Server = _host.GetTestServer();
        }

        /// <inheritdoc />
        public async Task DisposeAsync()
        {
            if (_host != null)
            {
                await _host.StopAsync();
                _host.Dispose();
            }
        }

        /// <summary>
        /// Creates a client connected to the in-process server.
        /// </summary>
        /// <returns>A connected HTTP client.</returns>
        public HttpClient CreateClient() => Server.CreateClient();

        /// <summary>
        /// Creates a short-lived JWT signed for the supplied tenant.
        /// </summary>
        /// <param name="tenant">The tenant that owns the token.</param>
        /// <returns>An encoded JWT.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="tenant"/> is unknown.</exception>
        public static string CreateToken(string tenant)
        {
            if (!SigningKeys.TryGetValue(tenant, out string signingKey))
            {
                throw new ArgumentOutOfRangeException(nameof(tenant), tenant, "The tenant is not configured.");
            }

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
            var token = new JwtSecurityToken(
                issuer: $"https://{tenant}.issuer.example.org",
                audience: $"https://{tenant}.audience.example.org",
                claims: new[] { new Claim(ClaimTypes.NameIdentifier, $"{tenant}-user") },
                notBefore: DateTime.UtcNow.AddMinutes(-1),
                expires: DateTime.UtcNow.AddMinutes(10),
                signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private static void ConfigureServices(IServiceCollection services)
        {
            services.AddLogging();
            services.AddSingleton(TimeProvider.System);
            services.AddRouting();
            services.AddControllers(options => options.Filters.Add(new TenantNeutralGlobalFilter()))
                .AddApplicationPart(typeof(TenantIsolationProbeController).Assembly);

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    // Any successful authentication must use the tenant provider's post-configuration, not
                    // this deliberately unusable root configuration.
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(
                            Encoding.UTF8.GetBytes("root-key-that-must-never-validate-any!!")),
                        ValidateIssuer = false,
                        ValidateAudience = false,
                    };
                });
            services.AddAuthorization();

            services.AddScoped<TenantScopedProbe>(_ => new TenantScopedProbe("root"));

            services.AddSingleton<ITenantResolver, HostHeaderTenantResolver>();
            services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();
            services.AddSingleton<ITenantRegistry>(CreateRegistry());
            services.AddSingleton(
                new TenantSharedServiceRegistry()
                    .ShareWithTenants<Microsoft.Extensions.Logging.ILoggerFactory>()
                    .ShareWithTenants<Microsoft.Extensions.Logging.ILoggerProvider>());

            var hostedServicePolicy = new TenantHostedServicePolicy();
            hostedServicePolicy.Set(
                "Microsoft.AspNetCore.DataProtection.Internal.DataProtectionHostedService",
                TenantHostedServiceDisposition.Shared);
            hostedServicePolicy.Set(
                "Microsoft.AspNetCore.Hosting.GenericWebHostService",
                TenantHostedServiceDisposition.Shared);
            services.AddSingleton<ITenantHostedServicePolicy>(hostedServicePolicy);
            services.AddSingleton<ITenantServiceConfigurator, JwtPerTenantConfigurator>();
            services.AddSingleton<ITenantServiceConfigurator, ProbePerTenantConfigurator>();
            services.AddSingleton(
                Options.Create(
                    new TenantContainerCacheOptions
                    {
                        MaxResidentTenants = 8,
                        IdleTimeout = TimeSpan.FromMinutes(30),
                    }));
            services.AddSingleton<ITenantContainerFactory, TenantContainerFactory>();
            services.AddSingleton<ITenantContainerCache, TenantContainerCache>();
            services.AddSingleton<ITenantServiceBlueprint>(new TenantServiceBlueprint(services));
        }

        private static void Configure(IApplicationBuilder app)
        {
            app.UseFhirTenancy();
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseEndpoints(endpoints => endpoints.MapControllers());
        }

        private static InMemoryTenantRegistry CreateRegistry()
        {
            var registry = new InMemoryTenantRegistry();
            registry.Add(
                new TenantDescriptor(
                    new TenantId("alpha"),
                    new Uri($"https://{AlphaHost}"),
                    new Dictionary<string, string>
                    {
                        [TenantDescriptorProperties.Authority] = "https://alpha.issuer.example.org",
                        [TenantDescriptorProperties.Audience] = "https://alpha.audience.example.org",
                    }));
            registry.Add(
                new TenantDescriptor(
                    new TenantId("beta"),
                    new Uri($"https://{BetaHost}"),
                    new Dictionary<string, string>
                    {
                        [TenantDescriptorProperties.Authority] = "https://beta.issuer.example.org",
                        [TenantDescriptorProperties.Audience] = "https://beta.audience.example.org",
                    }));

            return registry;
        }

        /// <summary>
        /// A scoped service whose tenant name proves that a controller action used
        /// <see cref="HttpContext.RequestServices"/>.
        /// </summary>
        public sealed class TenantScopedProbe
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="TenantScopedProbe"/> class.
            /// </summary>
            /// <param name="tenantName">The tenant name supplied by the tenant container.</param>
            public TenantScopedProbe(string tenantName)
            {
                TenantName = tenantName;
            }

            /// <summary>
            /// Gets the tenant name supplied by the tenant container.
            /// </summary>
            public string TenantName { get; }
        }

        /// <summary>
        /// A deliberately tenant-neutral global filter. Its explicit registration makes the MVC filter
        /// inspection probe non-vacuous without allowing a filter instance to capture tenant state.
        /// </summary>
        public sealed class TenantNeutralGlobalFilter : IAsyncActionFilter
        {
            /// <inheritdoc />
            public async Task OnActionExecutionAsync(
                ActionExecutingContext context,
                ActionExecutionDelegate next)
            {
                context.HttpContext.Response.Headers[MvcFilterHeaderName] = MvcFilterHeaderValue;
                await next();
            }
        }

        private sealed class InMemoryTenantRegistry : ITenantRegistry
        {
            private readonly Dictionary<TenantId, TenantDescriptor> _tenants = new();

            public IReadOnlyCollection<TenantDescriptor> Tenants => _tenants.Values;

            public void Add(TenantDescriptor tenant)
            {
                _tenants.Add(tenant.TenantId, tenant);
            }

            public bool TryGetTenant(TenantId tenantId, out TenantDescriptor descriptor)
            {
                return _tenants.TryGetValue(tenantId, out descriptor);
            }
        }

        private sealed class ProbePerTenantConfigurator : ITenantServiceConfigurator
        {
            public void Configure(IServiceCollection services, TenantDescriptor tenant)
            {
                services.RemoveAll<TenantScopedProbe>();
                services.AddScoped(_ => new TenantScopedProbe(tenant.TenantId.ToString()));
            }
        }

        private sealed class JwtPerTenantConfigurator : ITenantServiceConfigurator
        {
            public void Configure(IServiceCollection services, TenantDescriptor tenant)
            {
                string tenantName = tenant.TenantId.ToString();
                services.PostConfigure<JwtBearerOptions>(
                    JwtBearerDefaults.AuthenticationScheme,
                    options =>
                    {
                        options.TokenValidationParameters = new TokenValidationParameters
                        {
                            ValidateIssuerSigningKey = true,
                            IssuerSigningKey = new SymmetricSecurityKey(
                                Encoding.UTF8.GetBytes(SigningKeys[tenantName])),
                            ValidateIssuer = true,
                            ValidIssuer = tenant.Properties[TenantDescriptorProperties.Authority],
                            ValidateAudience = true,
                            ValidAudience = tenant.Properties[TenantDescriptorProperties.Audience],
                            ValidateLifetime = true,
                        };
                    });
            }
        }
    }
}
