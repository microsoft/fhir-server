// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Registration;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Tenancy;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Registration
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class FhirServerTenancyRegistrationTests
    {
        [Fact]
        public void GivenTenancyDisabled_WhenServicesAreRegistered_ThenNoTenancyRegistrationsAreAdded()
        {
            IServiceCollection services = CreateBaseServices();
            int originalServiceCount = services.Count;

            services.AddFhirServerTenancy(new TenancyConfiguration());

            Assert.Equal(originalServiceCount, services.Count);
            Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(TenantContainerCache));
            Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(ITenantContainerCache));
            Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(ITenantContainerFactory));
            Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(ITenantServiceBlueprint));
            Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(TenantContainerSweeper));
            Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IHostedService));
        }

        [Fact]
        public async Task GivenTenancyEnabled_WhenServicesAreRegistered_ThenSingletonAliasesResolveToTheSameRootInstancesWithoutRecursiveOwnership()
        {
            IServiceCollection services = RegisterEnabledServices();

            ServiceDescriptor concreteCacheDescriptor = GetRequiredDescriptor<TenantContainerCache>(services);
            ServiceDescriptor cacheAliasDescriptor = GetRequiredDescriptor<ITenantContainerCache>(services);
            ServiceDescriptor factoryDescriptor = GetRequiredDescriptor<ITenantContainerFactory>(services);
            ServiceDescriptor hostedServiceDescriptor = GetRequiredDescriptor<IHostedService>(services);
            ServiceDescriptor blueprintDescriptor = GetRequiredDescriptor<ITenantServiceBlueprint>(services);

            Assert.Equal(ServiceLifetime.Singleton, concreteCacheDescriptor.Lifetime);
            Assert.Equal(typeof(TenantContainerCache), concreteCacheDescriptor.ImplementationType);

            Assert.Equal(ServiceLifetime.Singleton, cacheAliasDescriptor.Lifetime);
            Assert.Null(cacheAliasDescriptor.ImplementationType);
            Assert.NotNull(cacheAliasDescriptor.ImplementationFactory);

            Assert.Equal(ServiceLifetime.Singleton, factoryDescriptor.Lifetime);
            Assert.Equal(typeof(TenantContainerFactory), factoryDescriptor.ImplementationType);

            Assert.Equal(ServiceLifetime.Singleton, hostedServiceDescriptor.Lifetime);
            Assert.Equal(typeof(TenantContainerSweeper), hostedServiceDescriptor.ImplementationType);

            Assert.Equal(ServiceLifetime.Singleton, blueprintDescriptor.Lifetime);
            Assert.NotNull(blueprintDescriptor.ImplementationInstance);

            await using var rootProvider = BuildProvider(services);
            TenantContainerCache rootConcreteCache = rootProvider.GetRequiredService<TenantContainerCache>();
            ITenantContainerCache rootInterfaceCache = rootProvider.GetRequiredService<ITenantContainerCache>();
            IHostedService rootHostedService = Assert.Single(rootProvider.GetServices<IHostedService>());

            Assert.Same(rootConcreteCache, rootInterfaceCache);
            Assert.IsType<TenantContainerSweeper>(rootHostedService);

            ITenantContainer tenantContainer =
                await rootProvider.GetRequiredService<ITenantContainerFactory>().CreateAsync(CreateTenant("alpha"), CancellationToken.None);

            try
            {
                Assert.Same(rootConcreteCache, Resolve<TenantContainerCache>(tenantContainer));
                Assert.Same(rootInterfaceCache, Resolve<ITenantContainerCache>(tenantContainer));
                Assert.Empty(ResolveAll<IHostedService>(tenantContainer));
            }
            finally
            {
                await tenantContainer.DisposeAsync();
            }

            using ITenantLease lease = await rootInterfaceCache.AcquireAsync(CreateTenant("beta"), CancellationToken.None);
            Assert.Equal("beta", lease.TenantId.ToString());
        }

        [Fact]
        public void GivenTenancyEnabled_WhenServicesAreRegistered_ThenTheDefaultSharedSetIsExactAndTheSweeperIsShared()
        {
            IServiceCollection services = RegisterEnabledServices();

            ServiceDescriptor sharedRegistryDescriptor = GetRequiredDescriptor<TenantSharedServiceRegistry>(services);
            ServiceDescriptor hostedServicePolicyDescriptor = GetRequiredDescriptor<ITenantHostedServicePolicy>(services);

            Assert.Equal(ServiceLifetime.Singleton, sharedRegistryDescriptor.Lifetime);
            Assert.Equal(ServiceLifetime.Singleton, hostedServicePolicyDescriptor.Lifetime);
            Assert.NotNull(sharedRegistryDescriptor.ImplementationInstance);
            Assert.NotNull(hostedServicePolicyDescriptor.ImplementationInstance);

            var sharedRegistry = Assert.IsType<TenantSharedServiceRegistry>(sharedRegistryDescriptor.ImplementationInstance);
            var hostedServicePolicy = Assert.IsAssignableFrom<ITenantHostedServicePolicy>(hostedServicePolicyDescriptor.ImplementationInstance);

            var expectedSharedTypes = new HashSet<Type>
            {
                typeof(ILoggerFactory),
                typeof(ILoggerProvider),
                typeof(IConfiguration),
                typeof(IHostEnvironment),
                typeof(IWebHostEnvironment),
                typeof(IHostApplicationLifetime),
                typeof(IHttpClientFactory),
                typeof(IMeterFactory),
                typeof(IModelInfoProvider),
                typeof(ICompartmentDefinitionManager),
                typeof(ISearchParameterDefinitionSource),
            };

            HashSet<Type> actualSharedTypes = sharedRegistry.SharedServiceTypes.ToHashSet();

            Assert.Equal(expectedSharedTypes.Count, actualSharedTypes.Count);
            Assert.True(actualSharedTypes.SetEquals(expectedSharedTypes));
            Assert.DoesNotContain(typeof(ILogger<>), actualSharedTypes);
            Assert.DoesNotContain(typeof(IServer), actualSharedTypes);
            Assert.Equal(
                TenantHostedServiceDisposition.Shared,
                hostedServicePolicy.Classify(typeof(TenantContainerSweeper).FullName));
        }

        [Fact]
        public void GivenTenancyEnabled_WhenOptionsAreRegistered_ThenTheConfigurationMapsExactly()
        {
            var configuration = new TenancyConfiguration
            {
                Enabled = true,
                MaxResidentTenants = 17,
                IdleTimeout = TimeSpan.FromMinutes(23),
                SweepInterval = TimeSpan.FromSeconds(29),
            };

            IServiceCollection services = Register(configuration);

            ServiceDescriptor optionsDescriptor = Assert.Single(
                services,
                descriptor => descriptor.ServiceType == typeof(IOptions<TenantContainerCacheOptions>));

            Assert.Equal(ServiceLifetime.Singleton, optionsDescriptor.Lifetime);
            Assert.NotNull(optionsDescriptor.ImplementationInstance);

            var options = Assert.IsAssignableFrom<IOptions<TenantContainerCacheOptions>>(optionsDescriptor.ImplementationInstance);

            Assert.Equal(configuration.MaxResidentTenants, options.Value.MaxResidentTenants);
            Assert.Equal(configuration.IdleTimeout, options.Value.IdleTimeout);
            Assert.Equal(configuration.SweepInterval, options.Value.SweepInterval);
        }

        [Theory]
        [MemberData(nameof(GetInvalidEnabledConfigurations))]
        public void GivenTenancyEnabled_WhenAnOptionIsInvalid_ThenRegistrationFails(
            TenancyConfiguration configuration,
            string expectedParamName)
        {
            ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => Register(configuration));

            Assert.Equal(expectedParamName, exception.ParamName);
        }

        [Fact]
        public void GivenTenancyEnabled_WhenTheBlueprintInstanceIsCaptured_ThenLaterRegistrationsStillAppearInSnapshots()
        {
            IServiceCollection services = RegisterEnabledServices();

            ServiceDescriptor blueprintDescriptor = GetRequiredDescriptor<ITenantServiceBlueprint>(services);
            Assert.NotNull(blueprintDescriptor.ImplementationInstance);

            var blueprint = Assert.IsAssignableFrom<ITenantServiceBlueprint>(blueprintDescriptor.ImplementationInstance);

            services.AddSingleton<AddedAfterBlueprintService>();

            IReadOnlyList<ServiceDescriptor> snapshot = blueprint.CreateSnapshot();

            Assert.Contains(snapshot, descriptor => descriptor.ServiceType == typeof(ITenantContainerFactory));
            Assert.Contains(snapshot, descriptor => descriptor.ServiceType == typeof(AddedAfterBlueprintService));
        }

        public static IEnumerable<object[]> GetInvalidEnabledConfigurations()
        {
            yield return new object[]
            {
                new TenancyConfiguration
                {
                    Enabled = true,
                    MaxResidentTenants = 0,
                    IdleTimeout = TimeSpan.FromMinutes(1),
                    SweepInterval = TimeSpan.FromMinutes(1),
                },
                nameof(TenancyConfiguration.MaxResidentTenants),
            };

            yield return new object[]
            {
                new TenancyConfiguration
                {
                    Enabled = true,
                    MaxResidentTenants = -1,
                    IdleTimeout = TimeSpan.FromMinutes(1),
                    SweepInterval = TimeSpan.FromMinutes(1),
                },
                nameof(TenancyConfiguration.MaxResidentTenants),
            };

            yield return new object[]
            {
                new TenancyConfiguration
                {
                    Enabled = true,
                    MaxResidentTenants = 1,
                    IdleTimeout = TimeSpan.Zero,
                    SweepInterval = TimeSpan.FromMinutes(1),
                },
                nameof(TenancyConfiguration.IdleTimeout),
            };

            yield return new object[]
            {
                new TenancyConfiguration
                {
                    Enabled = true,
                    MaxResidentTenants = 1,
                    IdleTimeout = TimeSpan.FromTicks(-1),
                    SweepInterval = TimeSpan.FromMinutes(1),
                },
                nameof(TenancyConfiguration.IdleTimeout),
            };

            yield return new object[]
            {
                new TenancyConfiguration
                {
                    Enabled = true,
                    MaxResidentTenants = 1,
                    IdleTimeout = TimeSpan.FromMinutes(1),
                    SweepInterval = TimeSpan.Zero,
                },
                nameof(TenancyConfiguration.SweepInterval),
            };

            yield return new object[]
            {
                new TenancyConfiguration
                {
                    Enabled = true,
                    MaxResidentTenants = 1,
                    IdleTimeout = TimeSpan.FromMinutes(1),
                    SweepInterval = TimeSpan.FromTicks(-1),
                },
                nameof(TenancyConfiguration.SweepInterval),
            };

            yield return new object[]
            {
                new TenancyConfiguration
                {
                    Enabled = true,
                    MaxResidentTenants = 1,
                    IdleTimeout = TimeSpan.FromMinutes(1),
                    SweepInterval = TimeSpan.FromTicks(1),
                },
                nameof(TenancyConfiguration.SweepInterval),
            };

            yield return new object[]
            {
                new TenancyConfiguration
                {
                    Enabled = true,
                    MaxResidentTenants = 1,
                    IdleTimeout = TimeSpan.FromMinutes(1),
                    SweepInterval = TimeSpan.FromMilliseconds(uint.MaxValue),
                },
                nameof(TenancyConfiguration.SweepInterval),
            };
        }

        private static ServiceDescriptor GetRequiredDescriptor<TService>(IServiceCollection services)
        {
            return Assert.Single(services, descriptor => descriptor.ServiceType == typeof(TService));
        }

        private static ServiceProvider BuildProvider(IServiceCollection services)
        {
            return services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = false,
            });
        }

        private static IServiceCollection RegisterEnabledServices()
        {
            return Register(new TenancyConfiguration { Enabled = true });
        }

        private static IServiceCollection Register(TenancyConfiguration configuration)
        {
            IServiceCollection services = CreateBaseServices();

            services.AddFhirServerTenancy(configuration);

            return services;
        }

        private static IServiceCollection CreateBaseServices()
        {
            var services = new ServiceCollection();
            services.AddOptions();
            services.AddLogging();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();
            services.AddSingleton<ITenantRegistry, SingleTenantRegistry>();
            services.AddSingleton<IFhirServerInstanceConfiguration, FhirServerInstanceConfiguration>();
            services.AddHttpClient();

            return services;
        }

        private static T Resolve<T>(ITenantContainer container)
        {
            Assert.True(container.TryAcquire(out ITenantLease lease));

            using (lease)
            {
                return lease.Services.GetRequiredService<T>();
            }
        }

        private static IReadOnlyList<T> ResolveAll<T>(ITenantContainer container)
        {
            Assert.True(container.TryAcquire(out ITenantLease lease));

            using (lease)
            {
                return lease.Services.GetServices<T>().ToArray();
            }
        }

        private static TenantDescriptor CreateTenant(string tenantName)
        {
            return new TenantDescriptor(
                new TenantId(tenantName),
                new Uri($"https://{tenantName}.example"));
        }

        private sealed class AddedAfterBlueprintService
        {
        }
    }
}
