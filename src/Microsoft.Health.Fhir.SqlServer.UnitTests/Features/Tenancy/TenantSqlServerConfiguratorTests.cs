// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Features.Tenancy;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.SqlServer.Features.Tenancy;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer.Configs;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Tenancy
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class TenantSqlServerConfiguratorTests
    {
        private static readonly TenantDescriptor Alpha = new(new TenantId("alpha"));
        private static readonly TenantDescriptor Beta = new(new TenantId("beta"));

        private const string RootConnectionString =
            "Server=tcp:root.database.windows.net;Database=root;Authentication=Active Directory Workload Identity";

        private const string AlphaConnectionString =
            "Server=tcp:pool01.database.windows.net;Database=alpha;Authentication=Active Directory Workload Identity";

        private const string AlphaConnectionStringVariant =
            "Authentication=Active Directory Workload Identity;Database=alpha;Server=tcp:pool01.database.windows.net;Max Pool Size=99;Min Pool Size=7";

        private const string BetaConnectionString =
            "Server=tcp:pool01.database.windows.net;Database=beta;Authentication=Active Directory Workload Identity";

        [Fact]
        public async Task GivenRealSqlServerRegistrations_WhenATenantContainerIsBuilt_ThenTheTenantSpecificConfigurationReplacesTheRootRegistrations()
        {
            IServiceCollection services = CreateRootServices(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [Alpha.TenantId.ToString()] = AlphaConnectionString,
                });

            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IConfigureOptions<SqlServerDataStoreConfiguration>));
            Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(SqlServerDataStoreConfiguration));

            await using ServiceProvider rootProvider = BuildRootProvider(services);
            await using ITenantContainer container = await rootProvider
                .GetRequiredService<ITenantContainerFactory>()
                .CreateAsync(Alpha, CancellationToken.None);

            using ITenantLease lease = AcquireLease(container);

            SqlServerDataStoreConfiguration configuration = lease.Services.GetRequiredService<SqlServerDataStoreConfiguration>();
            IOptions<SqlServerDataStoreConfiguration> options = lease.Services.GetRequiredService<IOptions<SqlServerDataStoreConfiguration>>();
            IOptionsSnapshot<SqlServerDataStoreConfiguration> snapshot = lease.Services.GetRequiredService<IOptionsSnapshot<SqlServerDataStoreConfiguration>>();
            IOptionsMonitor<SqlServerDataStoreConfiguration> monitor = lease.Services.GetRequiredService<IOptionsMonitor<SqlServerDataStoreConfiguration>>();
            IOptionsFactory<SqlServerDataStoreConfiguration> factory = lease.Services.GetRequiredService<IOptionsFactory<SqlServerDataStoreConfiguration>>();

            Assert.Same(configuration, options.Value);
            Assert.Same(configuration, snapshot.Value);
            Assert.Same(configuration, monitor.CurrentValue);
            Assert.Same(configuration, factory.Create(Options.DefaultName));
            Assert.Equal(BuildExpectedConnectionString(AlphaConnectionString), configuration.ConnectionString);
            Assert.Equal(TimeSpan.FromMinutes(3), configuration.CommandTimeout);
            Assert.Equal(TimeSpan.FromMinutes(4), configuration.StatementTimeout);
            Assert.True(configuration.AllowDatabaseCreation);
            Assert.True(configuration.Initialize);
            Assert.Equal(20, configuration.MaxPoolSize);
            Assert.NotNull(configuration.SchemaOptions);
            Assert.True(configuration.SchemaOptions.AutomaticUpdatesEnabled);
            Assert.Equal(13, configuration.SchemaOptions.JobPollingFrequencyInSeconds);
            Assert.Empty(lease.Services.GetServices<IConfigureOptions<SqlServerDataStoreConfiguration>>());
            Assert.Empty(lease.Services.GetServices<IPostConfigureOptions<SqlServerDataStoreConfiguration>>());
            Assert.Empty(lease.Services.GetServices<IOptionsChangeTokenSource<SqlServerDataStoreConfiguration>>());
        }

        [Fact]
        public async Task GivenTenantConnectionStringVariants_WhenConfigured_ThenCanonicalPoolKeysAreBoundedPerTenant()
        {
            string alphaPoolKey = await ResolveTenantConnectionStringAsync(Alpha, AlphaConnectionString);
            string alphaVariantPoolKey = await ResolveTenantConnectionStringAsync(Alpha, AlphaConnectionStringVariant);
            string betaPoolKey = await ResolveTenantConnectionStringAsync(Beta, BetaConnectionString);

            var alphaBuilder = new SqlConnectionStringBuilder(alphaPoolKey);
            var betaBuilder = new SqlConnectionStringBuilder(betaPoolKey);

            Assert.Equal(alphaPoolKey, alphaVariantPoolKey);
            Assert.NotEqual(alphaPoolKey, betaPoolKey);
            Assert.Equal(20, alphaBuilder.MaxPoolSize);
            Assert.Equal(0, alphaBuilder.MinPoolSize);
            Assert.Equal(SqlAuthenticationMethod.ActiveDirectoryWorkloadIdentity, alphaBuilder.Authentication);
            Assert.Equal("alpha", alphaBuilder.InitialCatalog);
            Assert.Equal("beta", betaBuilder.InitialCatalog);
        }

        [Fact]
        public void GivenATenantWithNoOverride_WhenConfigured_ThenTheRootConnectionStringIsUsed()
        {
            var provider = new RootTenantConnectionStringProvider(
                Options.Create(new SqlServerDataStoreConfiguration { ConnectionString = RootConnectionString }));

            Assert.Equal(RootConnectionString, provider.GetConnectionString(Alpha));
        }

        private static string BuildExpectedConnectionString(string connectionString)
        {
            var builder = new SqlConnectionStringBuilder(connectionString)
            {
                MaxPoolSize = 20,
                MinPoolSize = 0,
            };

            return builder.ConnectionString;
        }

        private static ITenantLease AcquireLease(ITenantContainer container)
        {
            Assert.True(container.TryAcquire(out ITenantLease lease));
            return lease;
        }

        private static ServiceProvider BuildRootProvider(IServiceCollection services) =>
            services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false });

        private static IServiceCollection CreateRootServices(IReadOnlyDictionary<string, string> connectionStrings)
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITenantConnectionStringProvider>(
                new StubTenantConnectionStringProvider(connectionStrings));

            IFhirServerBuilder builder = new TestFhirServerBuilder(services);
            builder.AddSqlServer(configuration =>
            {
                configuration.ConnectionString = RootConnectionString;
                configuration.CommandTimeout = TimeSpan.FromMinutes(3);
                configuration.StatementTimeout = TimeSpan.FromMinutes(4);
                configuration.AllowDatabaseCreation = true;
                configuration.Initialize = true;
                configuration.SchemaOptions = new SqlServerSchemaOptions
                {
                    AutomaticUpdatesEnabled = true,
                    JobPollingFrequencyInSeconds = 13,
                };
            });

            services.RemoveAll<IHostedService>();
            services.AddSingleton(new TenantSharedServiceRegistry());
            services.AddSingleton<ITenantHostedServicePolicy>(new TenantHostedServicePolicy());
            services.AddSingleton<ITenantServiceBlueprint>(new TenantServiceBlueprint(services));
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<ITenantContainerFactory, TenantContainerFactory>();

            return services;
        }

        private static async Task<string> ResolveTenantConnectionStringAsync(TenantDescriptor tenant, string connectionString)
        {
            IServiceCollection services = CreateRootServices(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [tenant.TenantId.ToString()] = connectionString,
                });

            await using ServiceProvider rootProvider = BuildRootProvider(services);
            await using ITenantContainer container = await rootProvider
                .GetRequiredService<ITenantContainerFactory>()
                .CreateAsync(tenant, CancellationToken.None);

            using ITenantLease lease = AcquireLease(container);

            return lease.Services.GetRequiredService<SqlServerDataStoreConfiguration>().ConnectionString;
        }

        private sealed class StubTenantConnectionStringProvider : ITenantConnectionStringProvider
        {
            private readonly IReadOnlyDictionary<string, string> _connectionStrings;

            public StubTenantConnectionStringProvider(IReadOnlyDictionary<string, string> connectionStrings)
            {
                _connectionStrings = connectionStrings;
            }

            public string GetConnectionString(TenantDescriptor tenant) => _connectionStrings[tenant.TenantId.ToString()];
        }

        private sealed class TestFhirServerBuilder : IFhirServerBuilder
        {
            public TestFhirServerBuilder(IServiceCollection services)
            {
                Services = services;
            }

            public IServiceCollection Services { get; }
        }
    }
}
