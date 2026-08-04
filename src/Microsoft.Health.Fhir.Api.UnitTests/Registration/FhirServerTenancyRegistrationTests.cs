// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.Context;
using Microsoft.Health.Fhir.Api.Features.Tenancy;
using Microsoft.Health.Fhir.Api.Registration;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
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
        private const string HealthCheckPublisherHostedServiceFullName = "Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckPublisherHostedService";
        private static readonly string AuditEventTypeMappingFullName = typeof(AuditEventTypeMapping).FullName!;

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
            ITenantResolver rootResolver = rootProvider.GetRequiredService<ITenantResolver>();
            IHostedService rootHostedService = Assert.Single(rootProvider.GetServices<IHostedService>());

            Assert.Same(rootConcreteCache, rootInterfaceCache);
            Assert.IsType<HostHeaderTenantResolver>(rootResolver);
            Assert.IsType<TenantContainerSweeper>(rootHostedService);

            ITenantContainer tenantContainer =
                await rootProvider.GetRequiredService<ITenantContainerFactory>().CreateAsync(CreateTenant("alpha"), CancellationToken.None);

            try
            {
                Assert.Same(rootConcreteCache, Resolve<TenantContainerCache>(tenantContainer));
                Assert.Same(rootInterfaceCache, Resolve<ITenantContainerCache>(tenantContainer));
                Assert.Same(rootResolver, Resolve<ITenantResolver>(tenantContainer));
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
                typeof(ITenantResolver),
                typeof(IModelInfoProvider),
                typeof(ICompartmentDefinitionManager),
                typeof(IAuditEventTypeMapping),
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
        public void GivenTenancyEnabled_WhenServicesAreRegistered_ThenTenantResolverIsRegisteredOnceAsASingletonHostHeaderTenantResolver()
        {
            IServiceCollection services = RegisterEnabledServices();

            ServiceDescriptor descriptor = GetRequiredDescriptor<ITenantResolver>(services);

            Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
            Assert.Equal(typeof(HostHeaderTenantResolver), descriptor.ImplementationType);
        }

        [Fact]
        public void GivenACustomTenantResolverRegisteredBeforeTenancy_WhenServicesAreRegistered_ThenOnlyTheCustomDescriptorRemains()
        {
            IServiceCollection services = CreateBaseServices();
            var customResolver = new TestTenantResolver();

            services.AddSingleton<ITenantResolver>(customResolver);
            services.AddFhirServerTenancy(new TenancyConfiguration { Enabled = true });

            ServiceDescriptor descriptor = Assert.Single(
                services,
                registration => registration.ServiceType == typeof(ITenantResolver));

            Assert.Same(customResolver, descriptor.ImplementationInstance);
            Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        }

        [Fact]
        public void GivenAddFhirServerWithTenancyEnabled_WhenInspectingHostedServices_ThenDescriptorOrderPolicyCoverageAndAuditSharingMatchTheRealPath()
        {
            IServiceCollection services = new ServiceCollection();
            IConfiguration configuration = CreateAddFhirServerConfiguration(tenancyEnabled: true, securityEnabled: false);

            services.AddFhirServer(configuration);

            ServiceDescriptor tenantContainerCacheDescriptor = GetRequiredDescriptor<TenantContainerCache>(services);
            ServiceDescriptor tenantContainerCacheAliasDescriptor = GetRequiredDescriptor<ITenantContainerCache>(services);
            ServiceDescriptor tenantContainerFactoryDescriptor = GetRequiredDescriptor<ITenantContainerFactory>(services);
            ServiceDescriptor tenantBlueprintDescriptor = GetRequiredDescriptor<ITenantServiceBlueprint>(services);
            ServiceDescriptor tenantSharedRegistryDescriptor = GetRequiredDescriptor<TenantSharedServiceRegistry>(services);
            ServiceDescriptor tenantHostedServicePolicyDescriptor = GetRequiredDescriptor<ITenantHostedServicePolicy>(services);

            Assert.Equal(ServiceLifetime.Singleton, tenantContainerCacheDescriptor.Lifetime);
            Assert.Equal(typeof(TenantContainerCache), tenantContainerCacheDescriptor.ImplementationType);
            Assert.Equal(ServiceLifetime.Singleton, tenantContainerCacheAliasDescriptor.Lifetime);
            Assert.Null(tenantContainerCacheAliasDescriptor.ImplementationType);
            Assert.NotNull(tenantContainerCacheAliasDescriptor.ImplementationFactory);
            Assert.Equal(ServiceLifetime.Singleton, tenantContainerFactoryDescriptor.Lifetime);
            Assert.Equal(typeof(TenantContainerFactory), tenantContainerFactoryDescriptor.ImplementationType);
            Assert.Equal(ServiceLifetime.Singleton, tenantBlueprintDescriptor.Lifetime);
            Assert.IsType<TenantServiceBlueprint>(tenantBlueprintDescriptor.ImplementationInstance);

            IReadOnlyList<IndexedServiceDescriptor> hostedServiceDescriptors = GetHostedServiceDescriptors(services);
            IReadOnlyList<IndexedServiceDescriptor> concreteHostedImplementationDescriptors = GetConcreteHostedImplementationDescriptors(services);

            Assert.NotEmpty(hostedServiceDescriptors);
            Assert.Equal(hostedServiceDescriptors.Count, concreteHostedImplementationDescriptors.Count);

            TenantHostedServicePolicy tenantHostedServicePolicy = Assert.IsType<TenantHostedServicePolicy>(tenantHostedServicePolicyDescriptor.ImplementationInstance);
            AssertHostedServiceRegistrationPairings(
                hostedServiceDescriptors,
                concreteHostedImplementationDescriptors,
                tenantHostedServicePolicy);

            ServiceDescriptor auditEventTypeMappingDescriptor = GetRequiredConcreteHostedImplementationDescriptor(
                concreteHostedImplementationDescriptors,
                AuditEventTypeMappingFullName);
            Assert.Equal(typeof(IAuditEventTypeMapping), auditEventTypeMappingDescriptor.ServiceType);
            Assert.Equal(typeof(AuditEventTypeMapping), auditEventTypeMappingDescriptor.ImplementationType);
            Assert.Equal(AuditEventTypeMappingFullName, GetRequiredImplementationTypeName(auditEventTypeMappingDescriptor));
            Assert.Equal(
                TenantHostedServiceDisposition.Shared,
                tenantHostedServicePolicy.Classify(AuditEventTypeMappingFullName));
            Assert.Equal(
                TenantHostedServiceDisposition.Shared,
                tenantHostedServicePolicy.Classify(HealthCheckPublisherHostedServiceFullName));

            var tenantSharedServiceRegistry = Assert.IsType<TenantSharedServiceRegistry>(tenantSharedRegistryDescriptor.ImplementationInstance);
            Assert.Contains(typeof(IAuditEventTypeMapping), tenantSharedServiceRegistry.SharedServiceTypes);
        }

        [Fact]
        public void GivenAddFhirServerWithTenancyEnabled_WhenTheStartupFilterConfiguresThePipeline_ThenTenantMiddlewareIsInsertedFirstBeforeCorsRequestContextAndFhirAuthentication()
        {
            IReadOnlyList<RecordedMiddlewareComponent> components = GetConfiguredPipeline(tenancyEnabled: true);

            int tenantIndex = GetMiddlewareIndex(components, typeof(TenantMiddleware));
            int corsIndex = GetMiddlewareIndex(components, typeof(CorsMiddleware));
            int requestContextIndex = GetMiddlewareIndex(components, typeof(FhirRequestContextMiddleware));
            int beforeAuthenticationIndex = GetMiddlewareIndex(components, typeof(FhirRequestContextBeforeAuthenticationMiddleware));

            Assert.Equal(0, tenantIndex);
            Assert.True(tenantIndex < corsIndex);
            Assert.True(tenantIndex < requestContextIndex);
            Assert.True(tenantIndex < beforeAuthenticationIndex);
        }

        [Fact]
        public void GivenAddFhirServerWithTenancyDisabled_WhenTheStartupFilterConfiguresThePipeline_ThenTenantMiddlewareIsNotInserted()
        {
            IReadOnlyList<RecordedMiddlewareComponent> components = GetConfiguredPipeline(tenancyEnabled: false);

            Assert.DoesNotContain(components, component => component.Contains(typeof(TenantMiddleware)));
            Assert.Equal(0, GetMiddlewareIndex(components, typeof(CorsMiddleware)));
            GetMiddlewareIndex(components, typeof(FhirRequestContextMiddleware));
            GetMiddlewareIndex(components, typeof(FhirRequestContextBeforeAuthenticationMiddleware));
        }

        [Fact]
        public void GivenAddFhirServerWithForwardedHeadersAndTenancyDisabled_WhenTheStartupFilterConfiguresThePipeline_ThenCorsStillPrecedesForwardedHeadersAndTenantMiddlewareIsNotInserted()
        {
            IReadOnlyList<RecordedMiddlewareComponent> components = GetConfiguredPipeline(
                tenancyEnabled: false,
                forwardedHeadersEnabled: true);

            int corsIndex = GetMiddlewareIndex(components, typeof(CorsMiddleware));
            int forwardedHeadersIndex = GetMiddlewareIndex(components, typeof(ForwardedHeadersMiddleware));

            Assert.DoesNotContain(components, component => component.Contains(typeof(TenantMiddleware)));
            Assert.True(corsIndex < forwardedHeadersIndex);
        }

        [Fact]
        public void GivenAddFhirServerWithForwardedHeadersAndTenancyEnabled_WhenTheStartupFilterConfiguresThePipeline_ThenTenantResolutionStillRunsFirst()
        {
            IReadOnlyList<RecordedMiddlewareComponent> components = GetConfiguredPipeline(
                tenancyEnabled: true,
                forwardedHeadersEnabled: true);

            int forwardedHeadersIndex = GetMiddlewareIndex(components, typeof(ForwardedHeadersMiddleware));
            int tenantIndex = GetMiddlewareIndex(components, typeof(TenantMiddleware));

            Assert.Equal(0, tenantIndex);
            Assert.True(tenantIndex < forwardedHeadersIndex);
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

        private static IReadOnlyList<IndexedServiceDescriptor> GetHostedServiceDescriptors(IServiceCollection services)
        {
            return services
                .Select((descriptor, index) => new IndexedServiceDescriptor(descriptor, index))
                .Where(registration => registration.Descriptor.ServiceType == typeof(IHostedService))
                .ToArray();
        }

        private static IReadOnlyList<IndexedServiceDescriptor> GetConcreteHostedImplementationDescriptors(IServiceCollection services)
        {
            return services
                .Select((descriptor, index) => new IndexedServiceDescriptor(descriptor, index))
                .Where(registration =>
                    registration.Descriptor.ImplementationType != null &&
                    typeof(IHostedService).IsAssignableFrom(registration.Descriptor.ImplementationType))
                .ToArray();
        }

        private static void AssertHostedServiceRegistrationPairings(
            IReadOnlyList<IndexedServiceDescriptor> hostedServiceDescriptors,
            IReadOnlyList<IndexedServiceDescriptor> concreteHostedImplementationDescriptors,
            ITenantHostedServicePolicy tenantHostedServicePolicy)
        {
            int minimumConcreteIndex = 0;

            for (int index = 0; index < hostedServiceDescriptors.Count; index++)
            {
                IndexedServiceDescriptor hostedServiceDescriptor = hostedServiceDescriptors[index];
                IndexedServiceDescriptor concreteHostedImplementationDescriptor = concreteHostedImplementationDescriptors[index];

                AssertHostedServiceRegistrationPair(
                    concreteHostedImplementationDescriptor,
                    hostedServiceDescriptor,
                    minimumConcreteIndex);
                tenantHostedServicePolicy.Classify(
                    GetRequiredImplementationTypeName(concreteHostedImplementationDescriptor.Descriptor));

                minimumConcreteIndex = hostedServiceDescriptor.Index + 1;
            }
        }

        private static void AssertHostedServiceRegistrationPair(
            IndexedServiceDescriptor concreteHostedImplementationDescriptor,
            IndexedServiceDescriptor hostedServiceDescriptor,
            int minimumConcreteIndex)
        {
            ServiceDescriptor concreteDescriptor = concreteHostedImplementationDescriptor.Descriptor;
            ServiceDescriptor hostedDescriptor = hostedServiceDescriptor.Descriptor;

            Assert.Equal(ServiceLifetime.Singleton, concreteDescriptor.Lifetime);
            Assert.Equal(ServiceLifetime.Singleton, hostedDescriptor.Lifetime);
            Assert.Equal(typeof(IHostedService), hostedDescriptor.ServiceType);
            Assert.Null(hostedDescriptor.ImplementationInstance);
            Assert.InRange(
                concreteHostedImplementationDescriptor.Index,
                minimumConcreteIndex,
                hostedServiceDescriptor.Index);

            if (ReferenceEquals(concreteDescriptor, hostedDescriptor))
            {
                Assert.Null(hostedDescriptor.ImplementationFactory);
                return;
            }

            Assert.Null(hostedDescriptor.ImplementationType);
            Assert.NotNull(hostedDescriptor.ImplementationFactory);
        }

        private static ServiceDescriptor GetRequiredConcreteHostedImplementationDescriptor(
            IReadOnlyList<IndexedServiceDescriptor> concreteHostedImplementationDescriptors,
            string implementationTypeName)
        {
            return Assert.Single(
                concreteHostedImplementationDescriptors,
                registration => GetRequiredImplementationTypeName(registration.Descriptor) == implementationTypeName).Descriptor;
        }

        private static string GetRequiredImplementationTypeName(ServiceDescriptor descriptor)
        {
            return descriptor.ImplementationType?.FullName
                ?? throw new InvalidOperationException("Expected a concrete hosted implementation type.");
        }

        private static ServiceProvider BuildProvider(IServiceCollection services)
        {
            return services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = false,
            });
        }

        private static IReadOnlyList<RecordedMiddlewareComponent> GetConfiguredPipeline(
            bool tenancyEnabled,
            bool forwardedHeadersEnabled = false)
        {
            IConfiguration configuration = CreateAddFhirServerConfiguration(
                tenancyEnabled,
                securityEnabled: false,
                forwardedHeadersEnabled);
            var services = new ServiceCollection();
            var environment = new TestWebHostEnvironment();

            services.AddSingleton<IConfiguration>(configuration);
            services.AddSingleton<IWebHostEnvironment>(environment);

            services.AddFhirServer(configuration);

            using ServiceProvider provider = BuildProvider(services);
            IStartupFilter startupFilter = provider
                .GetServices<IStartupFilter>()
                .Single(filter => filter.GetType().Name == "FhirServerStartupFilter");

            var builder = new RecordingApplicationBuilder(provider);
            startupFilter.Configure(static _ => { })(builder);

            return builder.Components
                .Select((component, index) => new RecordedMiddlewareComponent(index, component))
                .ToArray();
        }

        private static int GetMiddlewareIndex(IReadOnlyList<RecordedMiddlewareComponent> components, Type middlewareType)
        {
            RecordedMiddlewareComponent component = Assert.Single(components, registration => registration.Contains(middlewareType));
            return component.Index;
        }

        private static IServiceCollection RegisterEnabledServices()
        {
            return Register(new TenancyConfiguration { Enabled = true });
        }

        private static IConfiguration CreateAddFhirServerConfiguration(
            bool tenancyEnabled,
            bool securityEnabled,
            bool forwardedHeadersEnabled = false)
        {
            return new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    { "ASPNETCORE_FORWARDEDHEADERS_ENABLED", forwardedHeadersEnabled.ToString() },
                    { "FhirServer:MultiTenantApplication:Enabled", tenancyEnabled.ToString() },
                    { "FhirServer:Security:Enabled", securityEnabled.ToString() },
                })
                .Build();
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

        private readonly record struct IndexedServiceDescriptor(ServiceDescriptor Descriptor, int Index);

        private sealed record RecordedMiddlewareComponent(int Index, Func<RequestDelegate, RequestDelegate> Component)
        {
            public bool Contains(Type middlewareType)
            {
                return GetCandidateTypes(Component.Target).Contains(middlewareType);
            }

            private static IReadOnlyList<Type> GetCandidateTypes(object value)
            {
                if (value == null)
                {
                    return Array.Empty<Type>();
                }

                object[] fieldValues = GetFieldValues(value).ToArray();

                return fieldValues
                    .Concat(fieldValues.SelectMany(GetFieldValues))
                    .OfType<Type>()
                    .Distinct()
                    .ToArray();
            }

            private static IEnumerable<object> GetFieldValues(object value)
            {
                foreach (FieldInfo field in value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    object fieldValue = field.GetValue(value);

                    if (fieldValue != null)
                    {
                        yield return fieldValue;

                        if (fieldValue is IEnumerable sequence and not string)
                        {
                            foreach (object item in sequence)
                            {
                                if (item != null)
                                {
                                    yield return item;
                                }
                            }
                        }
                    }
                }
            }
        }

        private sealed class RecordingApplicationBuilder : IApplicationBuilder
        {
            public RecordingApplicationBuilder(IServiceProvider applicationServices)
            {
                ApplicationServices = applicationServices;
            }

            public IServiceProvider ApplicationServices { get; set; }

            public IFeatureCollection ServerFeatures { get; } = new FeatureCollection();

            public IDictionary<string, object> Properties { get; } = new Dictionary<string, object>();

            public List<Func<RequestDelegate, RequestDelegate>> Components { get; } = new();

            public RequestDelegate Build()
            {
                RequestDelegate app = static _ => Task.CompletedTask;

                for (int index = Components.Count - 1; index >= 0; index--)
                {
                    app = Components[index](app);
                }

                return app;
            }

            public IApplicationBuilder New()
            {
                return new RecordingApplicationBuilder(ApplicationServices);
            }

            public IApplicationBuilder Use(Func<RequestDelegate, RequestDelegate> middleware)
            {
                Components.Add(middleware);
                return this;
            }
        }

        private sealed class TestTenantResolver : ITenantResolver
        {
            public bool TryResolve(HttpContext httpContext, out TenantId tenantId)
            {
                tenantId = TenantId.Default;
                return true;
            }
        }

        private sealed class TestWebHostEnvironment : IWebHostEnvironment
        {
            public string ApplicationName { get; set; } = "Microsoft.Health.Fhir.R4.Api.UnitTests";

            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

            public string ContentRootPath { get; set; } = Environment.CurrentDirectory;

            public string EnvironmentName { get; set; } = Environments.Production;

            public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

            public string WebRootPath { get; set; } = Environment.CurrentDirectory;
        }

        private sealed class AddedAfterBlueprintService
        {
        }
    }
}
