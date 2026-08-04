// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Features.Tenancy;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.SqlServer.Features.Tenancy;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Configs;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Registration
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class FhirServerBuilderSqlServerRegistrationExtensionsTests
    {
        private static readonly Type[] TenantSqlConfigurationServiceTypes =
        {
            typeof(SqlServerDataStoreConfiguration),
            typeof(IConfigureOptions<SqlServerDataStoreConfiguration>),
            typeof(IPostConfigureOptions<SqlServerDataStoreConfiguration>),
            typeof(IValidateOptions<SqlServerDataStoreConfiguration>),
            typeof(IOptions<SqlServerDataStoreConfiguration>),
            typeof(IOptionsSnapshot<SqlServerDataStoreConfiguration>),
            typeof(IOptionsMonitor<SqlServerDataStoreConfiguration>),
            typeof(IOptionsFactory<SqlServerDataStoreConfiguration>),
            typeof(IOptionsChangeTokenSource<SqlServerDataStoreConfiguration>),
        };

        private const string RootConnectionString =
            "Server=tcp:root.database.windows.net;Database=root;Authentication=Active Directory Workload Identity";

        private const string SchemaInitializerTypeName = "Microsoft.Health.SqlServer.Features.Schema.SchemaInitializer";

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

        [Fact]
        public void GivenTheRootRegistrations_WhenAddSqlServerRuns_ThenTheTenantAffectingSqlConfigurationShapeIsExact()
        {
            var services = new ServiceCollection();
            IFhirServerBuilder builder = new TestFhirServerBuilder(services);

            builder.AddSqlServer(configuration => configuration.ConnectionString = RootConnectionString);

            // Exact, mutation-resistant fence: of every tenant-affecting SQL configuration service type the
            // configurator knows how to replace, AddSqlServer currently registers only one ConfigureNamedOptions
            // instance. A newly added closed wrapper/hook changes this collection and fails the fence.
            ServiceDescriptor configurationDescriptor = Assert.Single(
                services,
                descriptor => TenantSqlConfigurationServiceTypes.Contains(descriptor.ServiceType));
            Assert.Equal(typeof(IConfigureOptions<SqlServerDataStoreConfiguration>), configurationDescriptor.ServiceType);
            Assert.Equal(ServiceLifetime.Singleton, configurationDescriptor.Lifetime);
            Assert.IsType<ConfigureNamedOptions<SqlServerDataStoreConfiguration>>(configurationDescriptor.ImplementationInstance);

            // The tenant-affecting SQL services are registered exactly once each, by type, at the root.
            Assert.Single(
                services,
                descriptor => descriptor.ServiceType == typeof(ITenantConnectionStringProvider) &&
                    descriptor.ImplementationType == typeof(RootTenantConnectionStringProvider));
            Assert.Single(
                services,
                descriptor => descriptor.ServiceType == typeof(ITenantServiceConfigurator) &&
                    descriptor.ImplementationType == typeof(TenantSqlServerConfigurator));
        }

        [Fact]
        public void GivenAddSqlServerAppliedTwice_WhenTheConfiguratorRegistrationsAreCounted_ThenExactlyOneConfiguratorIsRegistered()
        {
            var services = new ServiceCollection();
            IFhirServerBuilder builder = new TestFhirServerBuilder(services);

            builder.AddSqlServer(configuration => configuration.ConnectionString = RootConnectionString);
            builder.AddSqlServer(configuration => configuration.ConnectionString = RootConnectionString);

            // The connection string provider uses TryAdd, so it is idempotent under a repeated registration.
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ITenantConnectionStringProvider));

            // The configurator must be equally idempotent: a duplicated AddSqlServer would otherwise run the
            // tenant rebind twice inside every tenant container.
            Assert.Single(
                services,
                descriptor => descriptor.ServiceType == typeof(ITenantServiceConfigurator) &&
                    descriptor.ImplementationType == typeof(TenantSqlServerConfigurator));
        }

        [Fact]
        public void GivenTheRootHostedServices_WhenClassifiedByTheDefaultPolicy_ThenTheStrippedSetIsExactAndDrivenByMissingClassifications()
        {
            var services = new ServiceCollection();
            IFhirServerBuilder builder = new TestFhirServerBuilder(services);

            builder.AddSqlServer(configuration => configuration.ConnectionString = RootConnectionString);

            List<ServiceDescriptor> hostedServiceDescriptors = services
                .Where(descriptor => descriptor.ServiceType == typeof(IHostedService))
                .ToList();

            Assert.NotEmpty(hostedServiceDescriptors);

            List<string> implementationTypeNames = hostedServiceDescriptors
                .Select(descriptor => descriptor.ImplementationType?.FullName ?? descriptor.ImplementationInstance?.GetType().FullName)
                .OfType<string>()
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            int factoryRegisteredCount = hostedServiceDescriptors
                .Count(descriptor => descriptor.ImplementationType == null && descriptor.ImplementationInstance == null);

            var expectedImplementationTypeNames = new List<string>
            {
                "Microsoft.AspNetCore.DataProtection.Internal.DataProtectionHostedService",
                "Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckPublisherHostedService",
                "Microsoft.Health.Encryption.Customer.Health.CustomerKeyValidationBackgroundService",
                "Microsoft.Health.SqlServer.Api.Features.Schema.SchemaJobWorkerBackgroundService",
                SchemaInitializerTypeName,
            };
            expectedImplementationTypeNames.Sort(StringComparer.Ordinal);

            // Exact fence: pin the concrete hosted services the harness strips, plus the two factory-registered
            // descriptors whose implementation type is only known by resolution order.
            Assert.Equal(expectedImplementationTypeNames, implementationTypeNames);
            Assert.Equal(2, factoryRegisteredCount);

            // The reason the harness must remove IHostedService before building a tenant container: the default
            // policy has no classification for SchemaInitializer (nor several other SQL hosted services), so
            // TenantContainerFactory would throw while filtering them. This is why the harness strips hosted
            // services -- not to remove SQL consumers.
            var policy = new TenantHostedServicePolicy();
            Assert.Throws<TenantHostedServiceNotClassifiedException>(() => policy.Classify(SchemaInitializerTypeName));
        }

        [Fact]
        public void GivenTheRootRegistrations_WhenHostedServicesAreStripped_ThenTheSqlConsumersRemainRegistered()
        {
            var services = new ServiceCollection();
            IFhirServerBuilder builder = new TestFhirServerBuilder(services);

            builder.AddSqlServer(configuration => configuration.ConnectionString = RootConnectionString);

            services.RemoveAll<IHostedService>();

            Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IHostedService));

            // Removing IHostedService is a classification concern only; the SQL consumers a tenant container
            // depends on survive the strip.
            Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ISqlConnectionBuilder));
            Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(SqlConnectionWrapperFactory));
            Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ITenantServiceConfigurator));
            Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ITenantConnectionStringProvider));
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
