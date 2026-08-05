// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.Tenancy;
using Microsoft.Health.Fhir.Api.Registration;
using Microsoft.Health.Fhir.Core.Features.Context;
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
        /// A host that simulates an internal proxy address not registered in the tenant registry.
        /// Used to prove that <c>X-Forwarded-Host</c> is needed for correct external host resolution.
        /// </summary>
        public const string InternalProxyHost = "internal.proxy.example.org";

        /// <summary>
        /// The response header added by the tenant-neutral MVC global filter.
        /// </summary>
        public const string MvcFilterHeaderName = "X-Tenant-Isolation-Global-Filter";

        /// <summary>
        /// The tenant-neutral MVC global filter's response value.
        /// </summary>
        public const string MvcFilterHeaderValue = "tenant-neutral";

        /// <summary>
        /// The response header added by the MVC-activated tenant filter.
        /// </summary>
        public const string MvcActivatedFilterHeaderName = "X-Tenant-Isolation-Activated-Filter";

        /// <summary>
        /// The response header containing the executing MVC global filter's identity.
        /// </summary>
        public const string MvcGlobalFilterIdentityHeaderName = "X-Tenant-Isolation-Global-Filter-Identity";

        private static readonly IReadOnlyDictionary<string, string> SigningKeys =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["alpha"] = "alpha-signing-key-alpha-signing-key-alpha!!",
                ["beta"] = "beta-signing-key-beta-signing-key-beta!!!!",
            };

        private readonly int _maxResidentTenants;
        private readonly TimeSpan _idleTimeout;
        private IHost _forwardedHeadersDisabledHost;
        private IHost _forwardedHeadersEnabledHost;
        private IHost _host;
        private ProbeConfiguratorInvocationCounter _probeConfiguratorInvocationCounter;

        /// <summary>
        /// Initializes a new instance of the <see cref="TenantIsolationTestServerFixture"/> class using the
        /// default tenant container cache capacity of 8 resident tenants and a 30-minute idle timeout.
        /// </summary>
        public TenantIsolationTestServerFixture()
            : this(maxResidentTenants: 8, idleTimeout: TimeSpan.FromMinutes(30))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TenantIsolationTestServerFixture"/> class using the
        /// supplied tenant container cache capacity and idle timeout.
        /// </summary>
        /// <param name="maxResidentTenants">The maximum number of resident tenant containers.</param>
        /// <param name="idleTimeout">The idle timeout after which a resident tenant container becomes eligible for eviction.</param>
        internal TenantIsolationTestServerFixture(int maxResidentTenants, TimeSpan idleTimeout)
        {
            _maxResidentTenants = maxResidentTenants;
            _idleTimeout = idleTimeout;
        }

        /// <summary>
        /// Gets the in-process server.
        /// </summary>
        public TestServer Server { get; private set; }

        /// <summary>
        /// Gets the identity of the MVC global filter materialized by the root service provider.
        /// </summary>
        public string RootMvcGlobalFilterIdentity { get; private set; }

        /// <summary>
        /// Gets the total number of probe-configurator invocations across all tenants.
        /// </summary>
        public int ProbeConfiguratorInvocationCount => _probeConfiguratorInvocationCounter.TotalInvocationCount;

        /// <inheritdoc />
        public async Task InitializeAsync()
        {
            _host = await StartHostAsync(ConfigureServices, Configure);

            Server = _host.GetTestServer();
            _probeConfiguratorInvocationCounter =
                _host.Services.GetRequiredService<ProbeConfiguratorInvocationCounter>();
            RootMvcGlobalFilterIdentity = ReadGlobalFilterIdentity(_host.Services);

            _forwardedHeadersDisabledHost = await StartProductionHostAsync(forwardedHeadersEnabled: false);
            _forwardedHeadersEnabledHost = await StartProductionHostAsync(forwardedHeadersEnabled: true);
        }

        /// <inheritdoc />
        public async Task DisposeAsync()
        {
            await StopAndDisposeHostAsync(_forwardedHeadersDisabledHost);
            await StopAndDisposeHostAsync(_forwardedHeadersEnabledHost);
            await StopAndDisposeHostAsync(_host);
        }

        /// <summary>
        /// Creates a client connected to the in-process server.
        /// </summary>
        /// <returns>A connected HTTP client.</returns>
        public HttpClient CreateClient() => Server.CreateClient();

        /// <summary>
        /// Creates a client connected to the in-process server configured by the production startup filters
        /// with tenancy enabled and forwarded headers disabled.
        /// </summary>
        /// <returns>A connected HTTP client.</returns>
        public HttpClient CreateForwardedHeadersDisabledClient() => _forwardedHeadersDisabledHost.GetTestClient();

        /// <summary>
        /// Creates a client connected to the in-process server configured by the production startup filters
        /// with forwarded headers and tenancy enabled.
        /// </summary>
        /// <returns>A connected HTTP client.</returns>
        public HttpClient CreateForwardedHeadersEnabledClient() => _forwardedHeadersEnabledHost.GetTestClient();

        /// <summary>
        /// Gets the number of probe-configurator invocations recorded for a tenant.
        /// </summary>
        /// <param name="tenantName">The tenant name.</param>
        /// <returns>The number of probe-configurator invocations recorded for the tenant.</returns>
        public int GetProbeConfiguratorInvocationCount(string tenantName) =>
            _probeConfiguratorInvocationCounter.GetInvocationCount(tenantName);

        /// <summary>
        /// Resolves the identity of the MVC global filter that the named tenant's container materializes.
        /// </summary>
        /// <remarks>
        /// Acquires a lease on the tenant's real container from the root <see cref="TenantContainerCache"/> via
        /// the root <see cref="ITenantRegistry"/>, resolves the tenant's own <see cref="IOptions{MvcOptions}"/>,
        /// and releases the lease. A resident container is reused when available; otherwise normal cache admission
        /// constructs one.
        /// </remarks>
        /// <param name="tenantName">The tenant whose container should materialize the filter.</param>
        /// <returns>The tenant container's MVC global filter identity.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="tenantName"/> is not registered.</exception>
        public async Task<string> GetTenantMvcGlobalFilterIdentityAsync(string tenantName)
        {
            ITenantRegistry registry = _host.Services.GetRequiredService<ITenantRegistry>();
            if (!registry.TryGetTenant(new TenantId(tenantName), out TenantDescriptor descriptor))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tenantName),
                    tenantName,
                    "The tenant is not registered.");
            }

            TenantContainerCache cache = _host.Services.GetRequiredService<TenantContainerCache>();
            using ITenantLease lease = await cache.AcquireAsync(descriptor, CancellationToken.None);
            return ReadGlobalFilterIdentity(lease.Services);
        }

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

        private static string ReadGlobalFilterIdentity(IServiceProvider services) =>
            services
                .GetRequiredService<IOptions<MvcOptions>>()
                .Value
                .Filters
                .OfType<TenantNeutralGlobalFilter>()
                .Single()
                .Identity;

        private static Task<IHost> StartHostAsync(
            Action<IServiceCollection> configureServices,
            Action<IApplicationBuilder> configureApplication)
        {
            return new HostBuilder()
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder.UseTestServer();
                    webBuilder.ConfigureServices(configureServices);
                    webBuilder.Configure(configureApplication);
                })
                .StartAsync();
        }

        private static Task<IHost> StartProductionHostAsync(bool forwardedHeadersEnabled)
        {
            return new HostBuilder()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseTestServer();
                    webBuilder.ConfigureServices(
                        services => ConfigureProductionServices(services, forwardedHeadersEnabled));
                    webBuilder.Configure(ConfigureProductionApplication);
                })
                .StartAsync();
        }

        private static async Task StopAndDisposeHostAsync(IHost host)
        {
            if (host == null)
            {
                return;
            }

            await host.StopAsync();
            host.Dispose();
        }

        private void ConfigureServices(IServiceCollection services)
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

            services.AddSingleton<ITenantServiceConfigurator, JwtPerTenantConfigurator>();
            services.AddSingleton<ITenantServiceConfigurator, ProbePerTenantConfigurator>();
            services.AddSingleton<ITenantServiceConfigurator, TenantInstanceConfigurationConfigurator>();
            ConfigureTenantServices(services, _maxResidentTenants, _idleTimeout);
        }

        private static void ConfigureProductionServices(
            IServiceCollection services,
            bool forwardedHeadersEnabled)
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string>
                    {
                        ["ASPNETCORE_FORWARDEDHEADERS_ENABLED"] = forwardedHeadersEnabled.ToString(),
                        ["DataStore"] = "SqlServer",
                        ["FhirServer:MultiTenantApplication:Enabled"] = bool.FalseString,
                        ["FhirServer:Security:Enabled"] = bool.FalseString,
                    })
                .Build();

            services.AddSingleton(configuration);
            services.AddFhirServer(configuration);
            services.Configure<ForwardedHeadersOptions>(
                options => options.ForwardedHeaders |= ForwardedHeaders.XForwardedHost);

            // Use AddFhirServer for its production startup filter, but replace its registration-time
            // disabled-tenancy options so this focused host can supply the smaller tenant service graph below.
            services.RemoveAll<IOptions<FhirServerConfiguration>>();
            services.AddSingleton(
                Options.Create(
                    new FhirServerConfiguration
                    {
                        MultiTenantApplication = { Enabled = true },
                    }));

            // Production hosted services are not started by this request pipeline probe. TestHost adds its
            // framework hosted services after this callback, so they remain in the tenant blueprint.
            services.RemoveAll<IHostedService>();
            services.RemoveAll<ITenantResolver>();
            services.RemoveAll<ITenantRegistry>();

            services.AddSingleton<ITenantServiceConfigurator, TenantInstanceConfigurationConfigurator>();
            services.AddSingleton<ITenantServiceConfigurator, ProbePerTenantConfigurator>();
            ConfigureTenantServices(
                services,
                8,
                TimeSpan.FromMinutes(30),
                shareRequestContextAccessor: true);
        }

        private static void ConfigureTenantServices(
            IServiceCollection services,
            int maxResidentTenants,
            TimeSpan idleTimeout,
            bool shareRequestContextAccessor = false)
        {
            services.TryAddSingleton<TimeProvider>(TimeProvider.System);
            services.AddScoped<TenantScopedProbe>(_ => new TenantScopedProbe("root"));
            services.AddSingleton<ITenantResolver, HostHeaderTenantResolver>();
            services.TryAddSingleton<ITenantContextAccessor, TenantContextAccessor>();
            services.AddSingleton<ITenantRegistry>(CreateRegistry());
            services.TryAddSingleton<IFhirServerInstanceConfiguration, FhirServerInstanceConfiguration>();
            var sharedServiceRegistry = new TenantSharedServiceRegistry()
                .ShareWithTenants<Microsoft.Extensions.Logging.ILoggerFactory>()
                .ShareWithTenants<Microsoft.Extensions.Logging.ILoggerProvider>();
            if (shareRequestContextAccessor)
            {
                sharedServiceRegistry.ShareWithTenants<RequestContextAccessor<IFhirRequestContext>>();
            }

            services.AddSingleton(sharedServiceRegistry);

            services.AddSingleton<ITenantHostedServicePolicy>(new TenantHostedServicePolicy());
            services.AddSingleton<ProbeConfiguratorInvocationCounter>();
            services.AddSingleton(
                Options.Create(
                    new TenantContainerCacheOptions
                    {
                        MaxResidentTenants = maxResidentTenants,
                        IdleTimeout = idleTimeout,
                    }));
            services.AddSingleton<ITenantContainerFactory, TenantContainerFactory>();
            services.AddSingleton<TenantContainerCache>();
            services.AddSingleton<ITenantContainerCache>(
                serviceProvider => serviceProvider.GetRequiredService<TenantContainerCache>());
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

        private static void ConfigureProductionApplication(IApplicationBuilder app)
        {
            // Production startup filters own the forwarded-header and tenancy middleware.
            app.UseRouting();
            app.UseEndpoints(
                endpoints => endpoints.MapGet(
                    "/forwarded-host/tenant",
                    async context =>
                    {
                        TenantScopedProbe probe =
                            context.RequestServices.GetRequiredService<TenantScopedProbe>();
                        await context.Response.WriteAsJsonAsync(new { tenant = probe.TenantName });
                    }));
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

        private static string GetSigningKey(string tenantName) =>
            SigningKeys.TryGetValue(tenantName, out string signingKey)
                ? signingKey
                : $"tenant-memory-harness-{tenantName}-signing-key";

        /// <summary>
        /// A scoped service whose tenant name proves MVC controller constructor injection, <c>FromServices</c>
        /// parameter resolution, and <c>TypeFilter</c> activation use <see cref="HttpContext.RequestServices"/>.
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
        /// inspection probe non-vacuous without allowing a filter instance to capture tenant state. Its
        /// <see cref="Identity"/> lets the test compare the executing instance with the root and tenant
        /// <see cref="IOptions{MvcOptions}"/> instances.
        /// </summary>
        public sealed class TenantNeutralGlobalFilter : IAsyncActionFilter
        {
            internal TenantNeutralGlobalFilter()
            {
                Identity = Guid.NewGuid().ToString("N");
            }

            /// <summary>
            /// Gets this filter instance's unique identity.
            /// </summary>
            public string Identity { get; }

            /// <inheritdoc />
            public async Task OnActionExecutionAsync(
                ActionExecutingContext context,
                ActionExecutionDelegate next)
            {
                context.HttpContext.Response.Headers[MvcFilterHeaderName] = MvcFilterHeaderValue;
                context.HttpContext.Response.Headers[MvcGlobalFilterIdentityHeaderName] = Identity;
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
            private readonly ProbeConfiguratorInvocationCounter _invocationCounter;

            public ProbePerTenantConfigurator(ProbeConfiguratorInvocationCounter invocationCounter)
            {
                _invocationCounter = invocationCounter;
            }

            public void Configure(IServiceCollection services, TenantDescriptor tenant)
            {
                string tenantName = tenant.TenantId.ToString();

                // TenantContainerFactory invokes this configurator exactly once per container construction
                // attempt, before it calls BuildServiceProvider, so this count equals the number of
                // construction attempts for the tenant.
                _invocationCounter.RecordInvocation(tenantName);
                services.RemoveAll<TenantScopedProbe>();
                services.AddScoped(_ => new TenantScopedProbe(tenantName));
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
                                Encoding.UTF8.GetBytes(GetSigningKey(tenantName))),
                            ValidateIssuer = true,
                            ValidIssuer = tenant.Properties[TenantDescriptorProperties.Authority],
                            ValidateAudience = true,
                            ValidAudience = tenant.Properties[TenantDescriptorProperties.Audience],
                            ValidateLifetime = true,
                        };
                    });
            }
        }

        /// <summary>
        /// Counts <see cref="ProbePerTenantConfigurator"/> invocations. Because the factory invokes that
        /// configurator once per container construction attempt, these counts double as per-tenant
        /// construction-attempt totals.
        /// </summary>
        private sealed class ProbeConfiguratorInvocationCounter
        {
            private readonly ConcurrentDictionary<string, int> _counts = new(StringComparer.Ordinal);
            private int _totalCount;

            public int TotalInvocationCount => Volatile.Read(ref _totalCount);

            public int GetInvocationCount(string tenantName)
            {
                return _counts.TryGetValue(tenantName, out int count) ? count : 0;
            }

            public void RecordInvocation(string tenantName)
            {
                _counts.AddOrUpdate(tenantName, 1, (_, count) => count + 1);
                Interlocked.Increment(ref _totalCount);
            }
        }
    }
}
