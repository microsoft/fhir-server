// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Features.Tenancy;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.SqlServer.Features.Tenancy;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer.Configs;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Registration
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class FhirServerBuilderSqlServerRegistrationExtensionsTests
    {
        private const string RootConnectionString =
            "Server=tcp:root.database.windows.net;Database=root;Authentication=Active Directory Workload Identity";

        [Fact]
        public void GivenRealSqlServerRegistration_WhenAddSqlServerRuns_ThenSqlConfigurationUsesTheObservedOptionsShape()
        {
            var services = new ServiceCollection();
            IFhirServerBuilder builder = new TestFhirServerBuilder(services);

            builder.AddSqlServer(configuration => configuration.ConnectionString = RootConnectionString);

            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IConfigureOptions<SqlServerDataStoreConfiguration>));
            Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(SqlServerDataStoreConfiguration));
            Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IOptions<SqlServerDataStoreConfiguration>));
        }

        [Fact]
        public void GivenNoHostOverride_WhenAddSqlServerRuns_ThenTheTenantConnectionStringProviderAndConfiguratorAreRegistered()
        {
            var services = new ServiceCollection();
            IFhirServerBuilder builder = new TestFhirServerBuilder(services);

            builder.AddSqlServer(configuration => configuration.ConnectionString = RootConnectionString);

            Assert.Contains(
                services,
                descriptor => descriptor.ServiceType == typeof(ITenantConnectionStringProvider) &&
                    descriptor.ImplementationType == typeof(RootTenantConnectionStringProvider));
            Assert.Contains(
                services,
                descriptor => descriptor.ServiceType == typeof(ITenantServiceConfigurator) &&
                    descriptor.ImplementationType == typeof(TenantSqlServerConfigurator));
        }

        [Fact]
        public void GivenAHostPreRegistration_WhenAddSqlServerRuns_ThenTheHostProviderWins()
        {
            var services = new ServiceCollection();
            var provider = new StubTenantConnectionStringProvider();
            services.AddSingleton<ITenantConnectionStringProvider>(provider);

            IFhirServerBuilder builder = new TestFhirServerBuilder(services);
            builder.AddSqlServer(configuration => configuration.ConnectionString = RootConnectionString);

            using ServiceProvider serviceProvider = services.BuildServiceProvider();

            Assert.Same(provider, serviceProvider.GetRequiredService<ITenantConnectionStringProvider>());
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ITenantConnectionStringProvider));
        }

        private sealed class StubTenantConnectionStringProvider : ITenantConnectionStringProvider
        {
            public string GetConnectionString(TenantDescriptor tenant) => tenant.TenantId.ToString();
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
