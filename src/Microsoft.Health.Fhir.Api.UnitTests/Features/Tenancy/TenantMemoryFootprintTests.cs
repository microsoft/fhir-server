// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Tenancy;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Tenancy
{
    /// <summary>
    /// Measures the managed-memory shape of 200 materialized TestHost tenant containers and the memory
    /// reclaimed by their explicit eviction.
    /// </summary>
    /// <remarks>
    /// This is deliberately a TestHost <c>AddControllers()</c> container-shape measurement, not the full
    /// production FHIR composition root. It materializes the documented JWT, MVC, authentication-handler,
    /// probe, and instance-configuration set in every container so the result does not merely measure DI
    /// descriptors. It must be reported beside, never instead of, the R4
    /// <c>SearchParameterDefinitionManager</c> projection, because that dominant manager is absent here.
    /// </remarks>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    [Collection(TenantMemoryFootprintCollection.Name)]
    public class TenantMemoryFootprintTests
    {
        private const int TenantCount = 200;
        private const int TrialCount = 3;
        private const long FatalBytesPerTenant = 50L * 1024 * 1024;
        private const long SupersededEstimateBytesPerTenant = 5L * 1024 * 1024;
        private static readonly FieldInfo BaseUriByTenantField =
            typeof(FhirServerInstanceConfiguration).GetField(
                "_baseUriByTenant",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly ITestOutputHelper _output;

        public TenantMemoryFootprintTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task GivenTwoHundredMaterializedTenantContainers_WhenMeasured_ThenTheirFootprintIsAttributableAndBounded()
        {
            var fixture = new TenantIsolationTestServerFixture(
                maxResidentTenants: TenantCount + 8,
                idleTimeout: TimeSpan.Zero);
            await fixture.InitializeAsync();

            try
            {
                TenantContainerCache cache = fixture.Server.Services.GetRequiredService<TenantContainerCache>();
                ITenantContextAccessor tenantContextAccessor =
                    fixture.Server.Services.GetRequiredService<ITenantContextAccessor>();
                IFhirServerInstanceConfiguration rootInstanceConfiguration =
                    fixture.Server.Services.GetRequiredService<IFhirServerInstanceConfiguration>();

                Assert.Equal(0, GetBaseUriEntryCount(rootInstanceConfiguration));

                await StabilizeFixtureAsync(
                    fixture,
                    cache,
                    tenantContextAccessor,
                    rootInstanceConfiguration);

                var trials = new List<ContainerTrialMeasurement>(TrialCount);
                for (int trial = 0; trial < TrialCount; trial++)
                {
                    trials.Add(
                        await MeasureTrialAsync(
                            trial,
                            cache,
                            tenantContextAccessor,
                            rootInstanceConfiguration));
                }

                foreach (ContainerTrialMeasurement trial in trials)
                {
                    ReportTrial(trial);
                }

                ContainerTrialMeasurement medianGrossTrial = trials
                    .OrderBy(trial => trial.GrossBytesPerTenant)
                    .ElementAt(TrialCount / 2);

                _output.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "Chosen statistic: median gross-per-tenant trial ({0}); its unmodified gross/reclaimed/residue tuple is reported below.",
                    medianGrossTrial.TrialNumber + 1));
                Report("gross per tenant", medianGrossTrial.GrossBytesPerTenant);
                Report("reclaimed per tenant after explicit eviction", medianGrossTrial.ReclaimedBytesPerTenant);
                Report("residue per tenant after explicit eviction", medianGrossTrial.ResidueBytesPerTenant);
                _output.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "Base-URI architecture/result: {0} tenant-owned FhirServerInstanceConfiguration instances each materialized one BaseUri entry; root dictionary entries after all builds and evictions: {1}.",
                    TenantCount,
                    GetBaseUriEntryCount(rootInstanceConfiguration)));
                _output.WriteLine(
                    "Materialized and retained through each construction measurement: IOptionsMonitor<JwtBearerOptions>.Get(Bearer), IOptions<MvcOptions>.Value, IAuthenticationSchemeProvider.GetSchemeAsync(Bearer), IAuthenticationHandlerProvider.GetHandlerAsync(Bearer) in a tenant scope, TenantScopedProbe in that scope, and IFhirServerInstanceConfiguration.");
                _output.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "Superseded 5 MB estimate comparison only: median gross {0} the {1:N0}-byte estimate; it is not an assertion.",
                    medianGrossTrial.GrossBytesPerTenant < SupersededEstimateBytesPerTenant ? "is within" : "is above",
                    SupersededEstimateBytesPerTenant));

                Assert.True(
                    medianGrossTrial.GrossBytesPerTenant < FatalBytesPerTenant,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Median gross TestHost container footprint {0:N0} bytes per tenant exceeds the {1:N0}-byte fatal bound.",
                        medianGrossTrial.GrossBytesPerTenant,
                        FatalBytesPerTenant));

                GC.KeepAlive(rootInstanceConfiguration);
            }
            finally
            {
                await fixture.DisposeAsync();
            }
        }

        private async Task StabilizeFixtureAsync(
            TenantIsolationTestServerFixture fixture,
            TenantContainerCache cache,
            ITenantContextAccessor tenantContextAccessor,
            IFhirServerInstanceConfiguration rootInstanceConfiguration)
        {
            using (HttpClient client = fixture.CreateClient())
            using (var request = new HttpRequestMessage(HttpMethod.Get, "/mvc/whoami"))
            {
                request.Headers.Host = TenantIsolationTestServerFixture.AlphaHost;

                using HttpResponseMessage response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            int httpRequestEvictionCount = cache.EvictionCount;
            await cache.EvictIdleAsync(CancellationToken.None);
            Assert.Equal(0, cache.Count);
            Assert.Equal(1, cache.EvictionCount - httpRequestEvictionCount);

            TenantDescriptor warmupTenant = CreateTenantDescriptor(trialNumber: -1, tenantNumber: 0);
            var warmup = new ResidentTenantGraphs();
            try
            {
                ITenantLease warmupLease = await cache.AcquireAsync(warmupTenant, CancellationToken.None);
                try
                {
                    MaterializedTenantServiceSet materializedWarmupServices =
                        await MaterializeTenantServicesAsync(
                            warmupLease,
                            warmupTenant,
                            tenantContextAccessor,
                            rootInstanceConfiguration);
                    warmup.Add(warmupLease, materializedWarmupServices);
                }
                catch
                {
                    warmupLease.Dispose();
                    throw;
                }

                Assert.Equal(1, cache.Count);
                Assert.Equal(0, GetBaseUriEntryCount(rootInstanceConfiguration));
            }
            finally
            {
                warmup.Dispose();
            }

            int warmupEvictionCount = cache.EvictionCount;
            await cache.EvictIdleAsync(CancellationToken.None);
            Assert.Equal(0, cache.Count);
            Assert.Equal(1, cache.EvictionCount - warmupEvictionCount);
            Assert.Equal(0, GetBaseUriEntryCount(rootInstanceConfiguration));

            ForceFullCollection();
            GC.KeepAlive(rootInstanceConfiguration);
        }

        private async Task<ContainerTrialMeasurement> MeasureTrialAsync(
            int trialNumber,
            TenantContainerCache cache,
            ITenantContextAccessor tenantContextAccessor,
            IFhirServerInstanceConfiguration rootInstanceConfiguration)
        {
            ForceFullCollection();
            long baselineBytes = GC.GetTotalMemory(forceFullCollection: true);
            var residentGraphs = new ResidentTenantGraphs();

            try
            {
                var baseUris = new HashSet<string>(StringComparer.Ordinal);

                for (int tenantNumber = 0; tenantNumber < TenantCount; tenantNumber++)
                {
                    TenantDescriptor tenant = CreateTenantDescriptor(trialNumber, tenantNumber);
                    Assert.NotNull(tenant.BaseUri);
                    Assert.True(
                        baseUris.Add(tenant.BaseUri.AbsoluteUri),
                        $"Tenant '{tenant.TenantId}' must have a unique non-null BaseUri.");

                    ITenantLease lease = await cache.AcquireAsync(tenant, CancellationToken.None);
                    try
                    {
                        MaterializedTenantServiceSet materializedTenantServices =
                            await MaterializeTenantServicesAsync(
                                lease,
                                tenant,
                                tenantContextAccessor,
                                rootInstanceConfiguration);
                        residentGraphs.Add(lease, materializedTenantServices);
                    }
                    catch
                    {
                        lease.Dispose();
                        throw;
                    }
                }

                Assert.Equal(TenantCount, residentGraphs.Count);
                Assert.Equal(TenantCount, cache.Count);
                Assert.Equal(TenantCount, residentGraphs.InstanceConfigurations.Count);
                Assert.Equal(
                    TenantCount,
                    residentGraphs.InstanceConfigurations
                        .Cast<object>()
                        .Distinct(ReferenceEqualityComparer.Instance)
                        .Count());
                Assert.All(
                    residentGraphs.InstanceConfigurations,
                    configuration =>
                    {
                        Assert.NotSame(rootInstanceConfiguration, configuration);
                        Assert.Equal(1, GetBaseUriEntryCount(configuration));
                    });
                Assert.Equal(0, GetBaseUriEntryCount(rootInstanceConfiguration));

                long constructedBytes = GC.GetTotalMemory(forceFullCollection: true);
                residentGraphs.KeepAlive();
                GC.KeepAlive(rootInstanceConfiguration);

                int evictionCountBeforeRelease = cache.EvictionCount;
                residentGraphs.Dispose();

                Assert.Equal(TenantCount, cache.Count);
                await cache.EvictIdleAsync(CancellationToken.None);
                Assert.Equal(0, cache.Count);
                Assert.Equal(TenantCount, cache.EvictionCount - evictionCountBeforeRelease);
                Assert.Equal(0, GetBaseUriEntryCount(rootInstanceConfiguration));

                long afterEvictionBytes = GC.GetTotalMemory(forceFullCollection: true);
                GC.KeepAlive(rootInstanceConfiguration);

                return new ContainerTrialMeasurement(
                    trialNumber,
                    baselineBytes,
                    constructedBytes,
                    afterEvictionBytes,
                    GetBaseUriEntryCount(rootInstanceConfiguration));
            }
            finally
            {
                residentGraphs.Dispose();

                if (cache.Count > 0)
                {
                    await cache.EvictIdleAsync(CancellationToken.None);
                }
            }
        }

        private static async Task<MaterializedTenantServiceSet> MaterializeTenantServicesAsync(
            ITenantLease lease,
            TenantDescriptor tenant,
            ITenantContextAccessor tenantContextAccessor,
            IFhirServerInstanceConfiguration rootInstanceConfiguration)
        {
            IServiceProvider services = lease.Services;
            IOptionsMonitor<JwtBearerOptions> jwtOptionsMonitor =
                services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>();
            JwtBearerOptions jwtOptions = jwtOptionsMonitor.Get(JwtBearerDefaults.AuthenticationScheme);
            Assert.Equal(
                tenant.Properties[TenantDescriptorProperties.Authority],
                jwtOptions.TokenValidationParameters.ValidIssuer);
            Assert.Equal(
                tenant.Properties[TenantDescriptorProperties.Audience],
                jwtOptions.TokenValidationParameters.ValidAudience);

            MvcOptions mvcOptions = services.GetRequiredService<IOptions<MvcOptions>>().Value;
            Assert.Contains(
                mvcOptions.Filters,
                filter => filter is TenantIsolationTestServerFixture.TenantNeutralGlobalFilter);

            IAuthenticationSchemeProvider authenticationSchemeProvider =
                services.GetRequiredService<IAuthenticationSchemeProvider>();
            AuthenticationScheme authenticationScheme =
                await authenticationSchemeProvider.GetSchemeAsync(JwtBearerDefaults.AuthenticationScheme);
            Assert.NotNull(authenticationScheme);
            Assert.Equal(JwtBearerDefaults.AuthenticationScheme, authenticationScheme.Name);

            IFhirServerInstanceConfiguration instanceConfiguration =
                services.GetRequiredService<IFhirServerInstanceConfiguration>();
            Assert.NotSame(rootInstanceConfiguration, instanceConfiguration);
            Assert.Equal(1, GetBaseUriEntryCount(instanceConfiguration));

            TenantId priorTenant = tenantContextAccessor.Current;
            try
            {
                tenantContextAccessor.SetCurrent(tenant.TenantId);
                Assert.Equal(tenant.BaseUri, instanceConfiguration.BaseUri);
            }
            finally
            {
                tenantContextAccessor.SetCurrent(priorTenant);
            }

            IServiceScope scope = services.CreateScope();
            try
            {
                var httpContext = new DefaultHttpContext
                {
                    RequestServices = scope.ServiceProvider,
                };
                httpContext.Request.Scheme = Uri.UriSchemeHttps;
                httpContext.Request.Host = new HostString(tenant.BaseUri.Host);

                IAuthenticationHandlerProvider authenticationHandlerProvider =
                    scope.ServiceProvider.GetRequiredService<IAuthenticationHandlerProvider>();
                IAuthenticationHandler authenticationHandler =
                    await authenticationHandlerProvider.GetHandlerAsync(
                        httpContext,
                        JwtBearerDefaults.AuthenticationScheme);
                Assert.NotNull(authenticationHandler);

                TenantIsolationTestServerFixture.TenantScopedProbe probe =
                    scope.ServiceProvider.GetRequiredService<TenantIsolationTestServerFixture.TenantScopedProbe>();
                Assert.Equal(tenant.TenantId.ToString(), probe.TenantName);

                return new MaterializedTenantServiceSet(
                    scope,
                    jwtOptionsMonitor,
                    jwtOptions,
                    mvcOptions,
                    authenticationSchemeProvider,
                    authenticationHandlerProvider,
                    httpContext,
                    authenticationHandler,
                    probe,
                    instanceConfiguration);
            }
            catch
            {
                scope.Dispose();
                throw;
            }
        }

        private static TenantDescriptor CreateTenantDescriptor(int trialNumber, int tenantNumber)
        {
            string tenantName = $"memory-{trialNumber:D2}-{tenantNumber:D3}";
            Uri baseUri = new($"https://{tenantName}.example.org/");

            return new TenantDescriptor(
                new TenantId(tenantName),
                baseUri,
                new Dictionary<string, string>
                {
                    [TenantDescriptorProperties.Authority] = $"https://{tenantName}.issuer.example.org",
                    [TenantDescriptorProperties.Audience] = $"https://{tenantName}.audience.example.org",
                });
        }

        private static int GetBaseUriEntryCount(IFhirServerInstanceConfiguration instanceConfiguration)
        {
            FhirServerInstanceConfiguration concreteConfiguration =
                Assert.IsType<FhirServerInstanceConfiguration>(instanceConfiguration);
            var baseUriByTenant =
                (ConcurrentDictionary<TenantId, Uri>)BaseUriByTenantField.GetValue(concreteConfiguration);

            return baseUriByTenant.Count;
        }

        private static void ForceFullCollection()
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        }

        private void ReportTrial(ContainerTrialMeasurement trial)
        {
            _output.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "Trial {0}: baseline={1:N0}; constructed={2:N0}; after explicit eviction={3:N0}; gross={4:N0} ({5:N0} per tenant); reclaimed={6:N0} ({7:N0} per tenant); residue={8:N0} ({9:N0} per tenant); root BaseUri entries={10}.",
                trial.TrialNumber + 1,
                trial.BaselineBytes,
                trial.ConstructedBytes,
                trial.AfterEvictionBytes,
                trial.GrossBytes,
                trial.GrossBytesPerTenant,
                trial.ReclaimedBytes,
                trial.ReclaimedBytesPerTenant,
                trial.ResidueBytes,
                trial.ResidueBytesPerTenant,
                trial.RootBaseUriEntryCount));
        }

        private void Report(string label, double bytes)
        {
            _output.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0}: {1:N0} bytes ({2:N2} MB).",
                label,
                bytes,
                bytes / (1024d * 1024d)));
        }

        private sealed class ResidentTenantGraphs : IDisposable
        {
            private readonly List<ITenantLease> _leases = [];
            private readonly List<MaterializedTenantServiceSet> _materializedServices = [];
            private readonly List<IFhirServerInstanceConfiguration> _instanceConfigurations = [];

            public int Count => _leases.Count;

            public IReadOnlyList<IFhirServerInstanceConfiguration> InstanceConfigurations => _instanceConfigurations;

            public void Add(ITenantLease lease, MaterializedTenantServiceSet materializedServices)
            {
                _leases.Add(lease);
                _materializedServices.Add(materializedServices);
                _instanceConfigurations.Add(materializedServices.InstanceConfiguration);
            }

            public void Dispose()
            {
                foreach (MaterializedTenantServiceSet materializedServices in _materializedServices)
                {
                    materializedServices.Dispose();
                }

                foreach (ITenantLease lease in _leases)
                {
                    lease.Dispose();
                }

                _materializedServices.Clear();
                _instanceConfigurations.Clear();
                _leases.Clear();
            }

            public void KeepAlive()
            {
                foreach (MaterializedTenantServiceSet materializedServices in _materializedServices)
                {
                    materializedServices.KeepAlive();
                }

                GC.KeepAlive(_leases);
                GC.KeepAlive(_instanceConfigurations);
            }
        }

        private sealed class MaterializedTenantServiceSet : IDisposable
        {
            private IServiceScope _scope;
            private IOptionsMonitor<JwtBearerOptions> _jwtOptionsMonitor;
            private JwtBearerOptions _jwtOptions;
            private MvcOptions _mvcOptions;
            private IAuthenticationSchemeProvider _authenticationSchemeProvider;
            private IAuthenticationHandlerProvider _authenticationHandlerProvider;
            private HttpContext _httpContext;
            private IAuthenticationHandler _authenticationHandler;
            private TenantIsolationTestServerFixture.TenantScopedProbe _probe;

            public MaterializedTenantServiceSet(
                IServiceScope scope,
                IOptionsMonitor<JwtBearerOptions> jwtOptionsMonitor,
                JwtBearerOptions jwtOptions,
                MvcOptions mvcOptions,
                IAuthenticationSchemeProvider authenticationSchemeProvider,
                IAuthenticationHandlerProvider authenticationHandlerProvider,
                HttpContext httpContext,
                IAuthenticationHandler authenticationHandler,
                TenantIsolationTestServerFixture.TenantScopedProbe probe,
                IFhirServerInstanceConfiguration instanceConfiguration)
            {
                _scope = scope;
                _jwtOptionsMonitor = jwtOptionsMonitor;
                _jwtOptions = jwtOptions;
                _mvcOptions = mvcOptions;
                _authenticationSchemeProvider = authenticationSchemeProvider;
                _authenticationHandlerProvider = authenticationHandlerProvider;
                _httpContext = httpContext;
                _authenticationHandler = authenticationHandler;
                _probe = probe;
                InstanceConfiguration = instanceConfiguration;
            }

            public IFhirServerInstanceConfiguration InstanceConfiguration { get; private set; }

            public void Dispose()
            {
                _scope?.Dispose();
                _scope = null;
                _jwtOptionsMonitor = null;
                _jwtOptions = null;
                _mvcOptions = null;
                _authenticationSchemeProvider = null;
                _authenticationHandlerProvider = null;
                _httpContext = null;
                _authenticationHandler = null;
                _probe = null;
                InstanceConfiguration = null;
            }

            public void KeepAlive()
            {
                GC.KeepAlive(_scope);
                GC.KeepAlive(_jwtOptionsMonitor);
                GC.KeepAlive(_jwtOptions);
                GC.KeepAlive(_mvcOptions);
                GC.KeepAlive(_authenticationSchemeProvider);
                GC.KeepAlive(_authenticationHandlerProvider);
                GC.KeepAlive(_httpContext);
                GC.KeepAlive(_authenticationHandler);
                GC.KeepAlive(_probe);
                GC.KeepAlive(InstanceConfiguration);
            }
        }

        private sealed class ContainerTrialMeasurement
        {
            public ContainerTrialMeasurement(
                int trialNumber,
                long baselineBytes,
                long constructedBytes,
                long afterEvictionBytes,
                int rootBaseUriEntryCount)
            {
                TrialNumber = trialNumber;
                BaselineBytes = baselineBytes;
                ConstructedBytes = constructedBytes;
                AfterEvictionBytes = afterEvictionBytes;
                RootBaseUriEntryCount = rootBaseUriEntryCount;
            }

            public int TrialNumber { get; }

            public long BaselineBytes { get; }

            public long ConstructedBytes { get; }

            public long AfterEvictionBytes { get; }

            public int RootBaseUriEntryCount { get; }

            public long GrossBytes => ConstructedBytes - BaselineBytes;

            public long ReclaimedBytes => ConstructedBytes - AfterEvictionBytes;

            public long ResidueBytes => AfterEvictionBytes - BaselineBytes;

            public double GrossBytesPerTenant => GrossBytes / (double)TenantCount;

            public double ReclaimedBytesPerTenant => ReclaimedBytes / (double)TenantCount;

            public double ResidueBytesPerTenant => ResidueBytes / (double)TenantCount;
        }
    }
}
