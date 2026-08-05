// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Tenancy;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Tenancy
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class TenantContainerFactoryTests
    {
        private static readonly TenantDescriptor Alpha = new(
            new TenantId("alpha"),
            new Uri("https://alpha.example.org"));

        [Fact]
        public async Task GivenANonSharedSingleton_WhenTwoTenantContainersAreBuilt_ThenEachGetsItsOwnInstance()
        {
            var harness = new Harness();
            harness.RootServices.AddSingleton<PerTenantService>();

            await using ITenantContainer alpha = await harness.CreateAsync(Alpha);
            await using ITenantContainer beta = await harness.CreateAsync(new TenantDescriptor(new TenantId("beta")));

            PerTenantService fromAlpha = Resolve<PerTenantService>(alpha);
            PerTenantService fromBeta = Resolve<PerTenantService>(beta);
            var fromRoot = harness.RootProvider.GetRequiredService<PerTenantService>();

            Assert.NotSame(fromAlpha, fromBeta);
            Assert.NotSame(fromAlpha, fromRoot);
        }

        [Fact]
        public async Task GivenASharedSingleton_WhenATenantContainerIsBuilt_ThenTheRootInstanceIsReused()
        {
            var harness = new Harness();
            harness.RootServices.AddSingleton<SharedService>();
            harness.SharedRegistry.ShareWithTenants<SharedService>();

            await using ITenantContainer alpha = await harness.CreateAsync(Alpha);

            Assert.Same(harness.RootProvider.GetRequiredService<SharedService>(), Resolve<SharedService>(alpha));
        }

        [Fact]
        public async Task GivenASingletonCapturesAScopedService_WhenTheFirstTenantContainerIsBuilt_ThenConstructionFails()
        {
            var harness = new Harness();
            harness.RootServices.AddScoped<ScopedDependency>();
            harness.RootServices.AddSingleton<SingletonCapturingScopedDependency>();
            harness.RootServices.AddSingleton<IHostedService, ScopeCapturingInitializerHostedService>();
            harness.Policy.Set(
                typeof(ScopeCapturingInitializerHostedService).FullName,
                TenantHostedServiceDisposition.PerTenantInitializer);

            InvalidOperationException exception =
                await Assert.ThrowsAsync<InvalidOperationException>(() => harness.CreateAsync(Alpha).AsTask());

            Assert.Contains("Cannot consume scoped service", exception.Message, StringComparison.Ordinal);
            Assert.Contains(typeof(ScopedDependency).FullName, exception.Message, StringComparison.Ordinal);
            Assert.Contains(
                typeof(SingletonCapturingScopedDependency).FullName,
                exception.Message,
                StringComparison.Ordinal);
        }

        [Fact]
        public async Task GivenASharedDisposableSingleton_WhenTheTenantContainerIsDisposed_ThenTheRootInstanceSurvives()
        {
            var harness = new Harness();
            harness.RootServices.AddSingleton<SharedDisposable>();
            harness.RootServices.AddSingleton<TenantOwnedDisposable>();
            harness.SharedRegistry.ShareWithTenants<SharedDisposable>();

            var rootShared = harness.RootProvider.GetRequiredService<SharedDisposable>();

            ITenantContainer alpha = await harness.CreateAsync(Alpha);
            var tenantOwned = Resolve<TenantOwnedDisposable>(alpha);
            Assert.Same(rootShared, Resolve<SharedDisposable>(alpha));

            await alpha.DisposeAsync();

            Assert.False(rootShared.IsDisposed);
            Assert.True(tenantOwned.IsDisposed);
        }

        [Fact]
        public async Task GivenTenancyInfrastructure_WhenATenantContainerIsBuilt_ThenItIsNotDuplicated()
        {
            var harness = new Harness();
            harness.RootServices.AddSingleton<IOptions<TenantContainerCacheOptions>>(
                Options.Create(new TenantContainerCacheOptions()));
            harness.RootServices.AddSingleton<TenantContainerCache>();
            harness.RootServices.AddSingleton<ITenantContainerCache, TenantContainerCache>();

            TenantContainerCache rootConcreteCache = harness.RootProvider.GetRequiredService<TenantContainerCache>();
            ITenantContainerCache rootInterfaceCache = harness.RootProvider.GetRequiredService<ITenantContainerCache>();

            try
            {
                await using ITenantContainer alpha = await harness.CreateAsync(Alpha);

                // The independent registrations prove that both service types are explicitly forwarded.
                // If either type is omitted from TenancyInfrastructureTypes, the child creates a new cache.
                Assert.NotSame(rootConcreteCache, rootInterfaceCache);
                Assert.Same(rootConcreteCache, Resolve<TenantContainerCache>(alpha));
                Assert.Same(rootInterfaceCache, Resolve<ITenantContainerCache>(alpha));
                Assert.Same(
                    harness.RootProvider.GetRequiredService<ITenantContainerFactory>(),
                    Resolve<ITenantContainerFactory>(alpha));
            }
            finally
            {
                await rootInterfaceCache.DisposeAsync();
                await rootConcreteCache.DisposeAsync();
            }
        }

        [Fact]
        public async Task GivenAPerTenantInitializer_WhenATenantContainerIsBuilt_ThenItIsStarted()
        {
            var harness = new Harness();
            harness.RootServices.AddSingleton<IHostedService, InitializerHostedService>();
            harness.Policy.Set(typeof(InitializerHostedService).FullName, TenantHostedServiceDisposition.PerTenantInitializer);

            await using ITenantContainer alpha = await harness.CreateAsync(Alpha);

            IEnumerable<IHostedService> hostedServices = Resolve<IEnumerable<IHostedService>>(alpha);

            InitializerHostedService initializer = Assert.IsType<InitializerHostedService>(Assert.Single(hostedServices));
            Assert.True(initializer.Started);
        }

        [Fact]
        public async Task GivenFactoryBasedHostedServices_WhenClassifiedByRegistrationPosition_ThenOnlyTheInitializerStarts()
        {
            var harness = new Harness();
            var tracker = new FactoryHostedServiceTracker();
            harness.RootServices.AddSingleton(tracker);
            harness.RootServices.AddSingleton<IHostedService>(
                serviceProvider => new FactoryBasedSharedHostedService(
                    serviceProvider.GetRequiredService<FactoryHostedServiceTracker>()));
            harness.RootServices.AddSingleton<IHostedService>(
                serviceProvider => new FactoryBasedInitializerHostedService(
                    serviceProvider.GetRequiredService<FactoryHostedServiceTracker>()));
            harness.Policy.Set(
                typeof(FactoryBasedSharedHostedService).FullName,
                TenantHostedServiceDisposition.Shared);
            harness.Policy.Set(
                typeof(FactoryBasedInitializerHostedService).FullName,
                TenantHostedServiceDisposition.PerTenantInitializer);

            await using ITenantContainer alpha = await harness.CreateAsync(Alpha);

            Assert.Collection(
                harness.RootProvider.GetServices<IHostedService>(),
                service => Assert.IsType<FactoryBasedSharedHostedService>(service),
                service => Assert.IsType<FactoryBasedInitializerHostedService>(service));

            IHostedService hostedService = Assert.Single(Resolve<IEnumerable<IHostedService>>(alpha));
            var initializer = Assert.IsType<FactoryBasedInitializerHostedService>(hostedService);
            Assert.True(initializer.Started);
            Assert.Equal(0, tracker.SharedStartCount);
            Assert.Equal(1, tracker.InitializerStartCount);
        }

        [Fact]
        public void GivenHostedServicesAreSharedWithTenants_WhenDeclared_ThenDeclarationFails()
        {
            var harness = new Harness();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => harness.SharedRegistry.ShareWithTenants<IHostedService>());

            Assert.Contains(typeof(IHostedService).FullName, exception.Message, StringComparison.Ordinal);
            Assert.Contains(typeof(ITenantHostedServicePolicy).FullName, exception.Message, StringComparison.Ordinal);
            Assert.Empty(harness.SharedRegistry.SharedServiceTypes);
        }

        [Fact]
        public async Task GivenAHostedServiceIsInternallyShared_WhenATenantContainerIsBuilt_ThenTheForwardGuardFailsConstruction()
        {
            var harness = new Harness();
            harness.RootServices.AddSingleton<IHostedService, InitializerHostedService>();
            harness.SharedRegistry.ShareHostedServiceImplementationWithTenants<IHostedService>();

            InvalidOperationException exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => harness.CreateAsync(Alpha).AsTask());

            Assert.Contains(typeof(IHostedService).FullName, exception.Message, StringComparison.Ordinal);
            Assert.Contains("must not be forwarded", exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(ITenantHostedServicePolicy), exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task GivenAConcretePerTenantInitializerIsInternallyShared_WhenATenantContainerIsBuilt_ThenForwardingFailsConstruction()
        {
            var harness = new Harness();
            harness.RootServices.AddSingleton<InitializerHostedService>();
            harness.RootServices.AddSingleton<IHostedService>(
                serviceProvider => serviceProvider.GetRequiredService<InitializerHostedService>());
            harness.SharedRegistry.ShareHostedServiceImplementationWithTenants<InitializerHostedService>();
            harness.Policy.Set(
                typeof(InitializerHostedService).FullName,
                TenantHostedServiceDisposition.PerTenantInitializer);

            InvalidOperationException exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => harness.CreateAsync(Alpha).AsTask());

            Assert.Contains(typeof(InitializerHostedService).FullName, exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(TenantHostedServiceDisposition.PerTenantInitializer), exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(ITenantHostedServicePolicy), exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task GivenForwardedServicesAreRegisteredBetweenHostedServices_WhenClassifiedByPosition_ThenTheInitializerIsStillSelected()
        {
            var harness = new Harness();
            var tracker = new FactoryHostedServiceTracker();
            harness.RootServices.AddSingleton(tracker);

            // A forwarded service registered between the two factory-based hosted services. Forward()
            // removes and re-appends its descriptors, which must not disturb the positional correlation
            // FilterHostedServices relies on.
            harness.RootServices.AddSingleton<IHostedService>(
                serviceProvider => new FactoryBasedSharedHostedService(
                    serviceProvider.GetRequiredService<FactoryHostedServiceTracker>()));
            harness.RootServices.AddSingleton<SharedService>();
            harness.SharedRegistry.ShareWithTenants<SharedService>();
            harness.RootServices.AddSingleton<IHostedService>(
                serviceProvider => new FactoryBasedInitializerHostedService(
                    serviceProvider.GetRequiredService<FactoryHostedServiceTracker>()));

            harness.Policy.Set(
                typeof(FactoryBasedSharedHostedService).FullName,
                TenantHostedServiceDisposition.Shared);
            harness.Policy.Set(
                typeof(FactoryBasedInitializerHostedService).FullName,
                TenantHostedServiceDisposition.PerTenantInitializer);

            await using ITenantContainer alpha = await harness.CreateAsync(Alpha);

            Assert.Same(harness.RootProvider.GetRequiredService<SharedService>(), Resolve<SharedService>(alpha));

            IHostedService hostedService = Assert.Single(Resolve<IEnumerable<IHostedService>>(alpha));
            Assert.IsType<FactoryBasedInitializerHostedService>(hostedService);
            Assert.Equal(0, tracker.SharedStartCount);
            Assert.Equal(1, tracker.InitializerStartCount);
        }

        [Fact]
        public async Task GivenARelocatedHostedService_WhenATenantContainerIsBuilt_ThenItIsNotPresent()
        {
            var harness = new Harness();
            harness.RootServices.AddSingleton<IHostedService, RelocatedHostedService>();
            harness.Policy.Set(typeof(RelocatedHostedService).FullName, TenantHostedServiceDisposition.Relocated);

            await using ITenantContainer alpha = await harness.CreateAsync(Alpha);

            Assert.Empty(Resolve<IEnumerable<IHostedService>>(alpha));
        }

        [Fact]
        public async Task GivenASharedHostedService_WhenATenantContainerIsBuilt_ThenItIsNotPresent()
        {
            var harness = new Harness();
            harness.RootServices.AddSingleton<IHostedService, SharedHostedService>();
            harness.Policy.Set(typeof(SharedHostedService).FullName, TenantHostedServiceDisposition.Shared);

            await using ITenantContainer alpha = await harness.CreateAsync(Alpha);

            Assert.Empty(Resolve<IEnumerable<IHostedService>>(alpha));
        }

        [Fact]
        public async Task GivenAnUnclassifiedHostedService_WhenATenantContainerIsBuilt_ThenConstructionFails()
        {
            var harness = new Harness();
            harness.RootServices.AddSingleton<IHostedService, UnclassifiedHostedService>();

            TenantHostedServiceNotClassifiedException exception =
                await Assert.ThrowsAsync<TenantHostedServiceNotClassifiedException>(
                    () => harness.CreateAsync(Alpha).AsTask());

            Assert.Equal(typeof(UnclassifiedHostedService).FullName, exception.HostedServiceTypeName);
        }

        [Fact]
        public async Task GivenAConfigurator_WhenATenantContainerIsBuilt_ThenItIsApplied()
        {
            var harness = new Harness();
            harness.RootServices.AddSingleton(new StampedService("root"));
            harness.Configurators.Add(new StampingConfigurator());

            await using ITenantContainer alpha = await harness.CreateAsync(Alpha);

            Assert.Equal("alpha", Resolve<StampedService>(alpha).Stamp);
            Assert.Equal("root", harness.RootProvider.GetRequiredService<StampedService>().Stamp);
        }

        [Fact]
        public async Task GivenACallerAmbientTenant_WhenAContainerIsCreatedAndDisposed_ThenTheEntireLifecycleObservesTheCreatedTenantAndRestoresTheCaller()
        {
            var harness = new Harness();
            var callerTenant = new TenantId("caller");
            var accessor = new TenantContextAccessorProbe();
            var probe = new AmbientTenantLifecycleProbe();
            harness.RootServices.AddSingleton<ITenantContextAccessor>(accessor);
            harness.RootServices.AddSingleton(probe);
            harness.RootServices.AddSingleton<IHostedService, AmbientTenantInitializer>();
            harness.Configurators.Add(new AmbientTenantConfigurator(accessor, probe));
            harness.Policy.Set(
                typeof(AmbientTenantInitializer).FullName,
                TenantHostedServiceDisposition.PerTenantInitializer);
            accessor.SetCurrent(callerTenant);

            ITenantContainer alpha = await harness.CreateAsync(Alpha);

            try
            {
                Assert.Same(accessor, probe.ConfiguratorRegistration);
                Assert.Same(accessor, Resolve<ITenantContextAccessor>(alpha));
                Assert.Equal(callerTenant, accessor.Current);
                Assert.Equal(
                    new[]
                    {
                        "construct:success",
                        "configure",
                        "construct:success",
                        "start:success",
                    },
                    probe.Observations.Select(observation => observation.Phase));
                Assert.All(
                    probe.Observations,
                    observation => Assert.Equal(Alpha.TenantId, observation.TenantId));
                Assert.All(
                    probe.InjectedAccessors,
                    injectedAccessor => Assert.Same(accessor, injectedAccessor));
            }
            finally
            {
                await alpha.DisposeAsync();
            }

            Assert.Equal("stop:success", probe.Observations.Last().Phase);
            Assert.Equal(Alpha.TenantId, probe.Observations.Last().TenantId);
            Assert.Equal(callerTenant, accessor.Current);
        }

        [Fact]
        public async Task GivenAConfiguratorFailure_WhenContainerCreationFails_ThenTheConfiguratorObservesTheCreatedTenantAndTheCallerIsRestored()
        {
            var harness = new Harness();
            var callerTenant = new TenantId("caller");
            var accessor = new TenantContextAccessorProbe();
            var probe = new AmbientTenantLifecycleProbe();
            var failure = new InvalidOperationException("configuration failed");
            harness.RootServices.AddSingleton<ITenantContextAccessor>(accessor);
            harness.Configurators.Add(new AmbientTenantConfigurator(accessor, probe, failure));
            accessor.SetCurrent(callerTenant);

            InvalidOperationException exception =
                await Assert.ThrowsAsync<InvalidOperationException>(() => harness.CreateAsync(Alpha).AsTask());

            Assert.Same(failure, exception);
            Assert.Same(accessor, probe.ConfiguratorRegistration);
            Assert.Collection(
                probe.Observations,
                observation =>
                {
                    Assert.Equal("configure", observation.Phase);
                    Assert.Equal(Alpha.TenantId, observation.TenantId);
                });
            Assert.Equal(callerTenant, accessor.Current);
        }

        [Fact]
        public async Task GivenCanceledInitializerStartup_WhenContainerCreationIsCanceled_ThenStartupObservesTheCreatedTenantAndTheCallerIsRestored()
        {
            var harness = new Harness();
            var callerTenant = new TenantId("caller");
            var accessor = new TenantContextAccessorProbe();
            var probe = new AmbientTenantLifecycleProbe();
            using var cancellationSource = new CancellationTokenSource();
            harness.RootServices.AddSingleton<ITenantContextAccessor>(accessor);
            harness.RootServices.AddSingleton(probe);
            harness.RootServices.AddSingleton<IHostedService, AmbientTenantCancellationInitializer>();
            harness.Policy.Set(
                typeof(AmbientTenantCancellationInitializer).FullName,
                TenantHostedServiceDisposition.PerTenantInitializer);
            accessor.SetCurrent(callerTenant);
            cancellationSource.Cancel();

            OperationCanceledException exception =
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => harness.CreateAsync(Alpha, cancellationSource.Token).AsTask());

            Assert.Equal(cancellationSource.Token, exception.CancellationToken);
            Assert.Contains(
                probe.Observations,
                observation =>
                    observation.Phase == "start:canceled" &&
                    observation.TenantId == Alpha.TenantId);
            Assert.All(
                probe.Observations,
                observation => Assert.Equal(Alpha.TenantId, observation.TenantId));
            Assert.All(
                probe.InjectedAccessors,
                injectedAccessor => Assert.Same(accessor, injectedAccessor));
            Assert.Equal(callerTenant, accessor.Current);
        }

        [Fact]
        public async Task GivenAnInitializerStartupFailure_WhenContainerCreationFails_ThenStartupAndCleanupObserveTheCreatedTenantAndTheCallerIsRestored()
        {
            var harness = new Harness();
            var callerTenant = new TenantId("caller");
            var accessor = new TenantContextAccessorProbe();
            var probe = new AmbientTenantLifecycleProbe();
            harness.RootServices.AddSingleton<ITenantContextAccessor>(accessor);
            harness.RootServices.AddSingleton(probe);
            harness.RootServices.AddSingleton<IHostedService, AmbientTenantInitializer>();
            harness.RootServices.AddSingleton<IHostedService, AmbientTenantStartupFailureInitializer>();
            harness.Policy.Set(
                typeof(AmbientTenantInitializer).FullName,
                TenantHostedServiceDisposition.PerTenantInitializer);
            harness.Policy.Set(
                typeof(AmbientTenantStartupFailureInitializer).FullName,
                TenantHostedServiceDisposition.PerTenantInitializer);
            accessor.SetCurrent(callerTenant);

            InvalidOperationException exception =
                await Assert.ThrowsAsync<InvalidOperationException>(() => harness.CreateAsync(Alpha).AsTask());

            Assert.Same(probe.StartupFailure, exception);
            Assert.Contains(
                probe.Observations,
                observation =>
                    observation.Phase == "start:success" &&
                    observation.TenantId == Alpha.TenantId);
            Assert.Contains(
                probe.Observations,
                observation =>
                    observation.Phase == "start:failure" &&
                    observation.TenantId == Alpha.TenantId);
            Assert.Contains(
                probe.Observations,
                observation =>
                    observation.Phase == "stop:success" &&
                    observation.TenantId == Alpha.TenantId);
            Assert.All(
                probe.Observations,
                observation => Assert.Equal(Alpha.TenantId, observation.TenantId));
            Assert.All(
                probe.InjectedAccessors,
                injectedAccessor => Assert.Same(accessor, injectedAccessor));
            Assert.Equal(callerTenant, accessor.Current);
        }

        [Fact]
        public async Task GivenAStopCancellation_WhenAContainerIsDisposed_ThenStopObservesTheCreatedTenantAndTheCallerIsRestored()
        {
            var harness = new Harness();
            var callerTenant = new TenantId("caller");
            var accessor = new TenantContextAccessorProbe();
            var probe = new AmbientTenantLifecycleProbe();
            harness.RootServices.AddSingleton<ITenantContextAccessor>(accessor);
            harness.RootServices.AddSingleton(probe);
            harness.RootServices.AddSingleton<IHostedService, AmbientTenantStopCancellationInitializer>();
            harness.Policy.Set(
                typeof(AmbientTenantStopCancellationInitializer).FullName,
                TenantHostedServiceDisposition.PerTenantInitializer);
            accessor.SetCurrent(callerTenant);

            ITenantContainer alpha = await harness.CreateAsync(Alpha);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => alpha.DisposeAsync().AsTask());

            Assert.Contains(
                probe.Observations,
                observation =>
                    observation.Phase == "stop:canceled" &&
                    observation.TenantId == Alpha.TenantId);
            Assert.Equal(callerTenant, accessor.Current);
        }

        [Fact]
        public async Task GivenTheInstanceConfigurationConfigurator_WhenATenantContainerIsBuilt_ThenSharedContextIsForwardedBeforeConfiguration()
        {
            var harness = new Harness();
            var priorTenant = new TenantId("caller");
            harness.RootServices.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();
            harness.RootServices.AddSingleton<ForwardingProbeConfigurator>();
            harness.RootServices.AddSingleton<ITenantServiceConfigurator>(
                serviceProvider => serviceProvider.GetRequiredService<ForwardingProbeConfigurator>());
            harness.RootServices.AddSingleton<TenantInstanceConfigurationConfigurator>();
            harness.RootServices.AddSingleton<ITenantServiceConfigurator>(
                serviceProvider => serviceProvider.GetRequiredService<TenantInstanceConfigurationConfigurator>());

            ITenantContextAccessor accessor = harness.RootProvider.GetRequiredService<ITenantContextAccessor>();
            ForwardingProbeConfigurator forwardingProbe = harness.RootProvider.GetRequiredService<ForwardingProbeConfigurator>();
            accessor.SetCurrent(priorTenant);

            await using ITenantContainer alpha = await harness.CreateAsync(Alpha);

            Assert.Same(accessor, forwardingProbe.ForwardedAccessor);
            Assert.Same(accessor, Resolve<ITenantContextAccessor>(alpha));
            Assert.Equal(priorTenant, accessor.Current);

            IFhirServerInstanceConfiguration configuration = Resolve<IFhirServerInstanceConfiguration>(alpha);

            try
            {
                accessor.SetCurrent(Alpha.TenantId);
                Assert.Equal(Alpha.BaseUri, configuration.BaseUri);
            }
            finally
            {
                accessor.SetCurrent(priorTenant);
            }

            Assert.Equal(priorTenant, accessor.Current);
        }

        [Fact]
        public async Task GivenMultipleSharedRegistrationsOfTheSameType_WhenATenantContainerIsBuilt_ThenTheRootInstancesAreForwardedInRegistrationOrder()
        {
            var harness = new Harness();
            var first = new OrderedSharedService("first");
            var second = new OrderedSharedService("second");
            harness.RootServices.AddSingleton(first);
            harness.RootServices.AddSingleton(second);
            harness.SharedRegistry.ShareWithTenants<OrderedSharedService>();

            await using ITenantContainer alpha = await harness.CreateAsync(Alpha);

            Assert.Collection(
                Resolve<IEnumerable<OrderedSharedService>>(alpha),
                service => Assert.Same(first, service),
                service => Assert.Same(second, service));
        }

        [Fact]
        public async Task GivenConfiguratorRegistrations_WhenATenantContainerIsBuilt_ThenTheyExecuteInRootRegistrationOrderAfterForwarding()
        {
            var harness = new Harness();
            var shared = new OrderedSharedService("shared");
            var events = new List<string>();
            harness.RootServices.AddSingleton(shared);
            harness.SharedRegistry.ShareWithTenants<OrderedSharedService>();
            harness.RootServices.AddSingleton<ITenantServiceConfigurator>(
                new RecordingConfigurator("first", events, shared, null));
            harness.RootServices.AddSingleton<ITenantServiceConfigurator>(
                new RecordingConfigurator("second", events, shared, "first"));

            await using ITenantContainer alpha = await harness.CreateAsync(Alpha);

            Assert.Equal(new[] { "first", "second" }, events);
            Assert.Same(shared, Resolve<OrderedSharedService>(alpha));
        }

        [Fact]
        public async Task GivenAnInitializerStartupFailure_WhenCleanupSucceeds_ThenCleanupOccursAndOriginalFailureIsPreserved()
        {
            var harness = new Harness();
            var failures = new FailurePlan();
            harness.RootServices.AddSingleton(failures);
            harness.RootServices.AddSingleton<IHostedService, StartupFailureHostedService>();
            harness.Policy.Set(
                typeof(StartupFailureHostedService).FullName,
                TenantHostedServiceDisposition.PerTenantInitializer);

            InvalidOperationException exception =
                await Assert.ThrowsAsync<InvalidOperationException>(() => harness.CreateAsync(Alpha).AsTask());

            Assert.Same(failures.StartupFailure, exception);
            Assert.Equal(1, failures.StartupServiceDisposeCount);
        }

        [Fact]
        public async Task GivenAnInitializerStartupFailureAndAnEmptyAggregateCleanupFailure_WhenCreationFails_ThenBothFailuresArePreservedAndCleanupOccurs()
        {
            var harness = new Harness();
            var failures = new FailurePlan();
            var cleanup = new EmptyAggregateStopHostedService();
            harness.RootServices.AddSingleton(failures);
            harness.RootServices.AddSingleton<IHostedService>(cleanup);
            harness.RootServices.AddSingleton<IHostedService, StartupFailureHostedService>();
            harness.Policy.Set(
                typeof(EmptyAggregateStopHostedService).FullName,
                TenantHostedServiceDisposition.PerTenantInitializer);
            harness.Policy.Set(
                typeof(StartupFailureHostedService).FullName,
                TenantHostedServiceDisposition.PerTenantInitializer);

            AggregateException exception =
                await Assert.ThrowsAsync<AggregateException>(() => harness.CreateAsync(Alpha).AsTask());

            Assert.Collection(
                exception.InnerExceptions,
                failure => Assert.Same(failures.StartupFailure, failure),
                failure => Assert.Same(cleanup.Failure, failure));
            Assert.Equal(1, cleanup.StopCallCount);
            Assert.Equal(1, failures.StartupServiceDisposeCount);
        }

        [Fact]
        public async Task GivenInitializerStartupAndCleanupFailures_WhenCreationFails_ThenAllFailuresAreAggregatedInOrder()
        {
            var harness = new Harness();
            var failures = new FailurePlan();
            harness.RootServices.AddSingleton(failures);
            harness.RootServices.AddSingleton<ThrowingCleanupDisposable>();
            harness.RootServices.AddSingleton<IHostedService, CleanupFailureHostedService>();
            harness.RootServices.AddSingleton<IHostedService, StartupFailureHostedService>();
            harness.Policy.Set(
                typeof(CleanupFailureHostedService).FullName,
                TenantHostedServiceDisposition.PerTenantInitializer);
            harness.Policy.Set(
                typeof(StartupFailureHostedService).FullName,
                TenantHostedServiceDisposition.PerTenantInitializer);

            AggregateException exception =
                await Assert.ThrowsAsync<AggregateException>(() => harness.CreateAsync(Alpha).AsTask());

            Assert.Collection(
                exception.InnerExceptions,
                failure => Assert.Same(failures.StartupFailure, failure),
                failure => Assert.Same(failures.StopFailure, failure),
                failure => Assert.Same(failures.DisposeFailure, failure));
        }

        [Fact]
        public async Task GivenNestedInitializerStartupAndCleanupAggregateFailures_WhenCreationFails_ThenEveryLeafFailureIsPreservedInOccurrenceOrder()
        {
            var harness = new Harness();
            var events = new List<string>();

            var startupLeaf1 = new InvalidOperationException("startup:1");
            var startupLeaf2 = new InvalidOperationException("startup:2");
            var startupLeaf3 = new InvalidOperationException("startup:3");
            var startupLeaf4 = new InvalidOperationException("startup:4");
            var startupEmptyAggregate = new AggregateException();
            var startupNestedAggregate = new AggregateException(startupLeaf2, startupEmptyAggregate, startupLeaf3);
            var startupFailure = new AggregateException(startupLeaf1, startupNestedAggregate, startupLeaf4);

            var cleanupLeaf1 = new InvalidOperationException("cleanup:1");
            var cleanupLeaf2 = new InvalidOperationException("cleanup:2");
            var cleanupLeaf3 = new InvalidOperationException("cleanup:3");
            var cleanupLeaf4 = new InvalidOperationException("cleanup:4");
            var cleanupEmptyAggregate = new AggregateException();
            var cleanupNestedAggregate = new AggregateException(cleanupLeaf2, cleanupEmptyAggregate, cleanupLeaf3);
            var cleanupFailure = new AggregateException(cleanupLeaf1, cleanupNestedAggregate, cleanupLeaf4);

            harness.RootServices.AddSingleton<IHostedService>(
                new NestedAggregateHostedService("first", events, null, cleanupFailure));
            harness.RootServices.AddSingleton<IHostedService>(
                new NestedAggregateHostedService("second", events, startupFailure, null));
            harness.Policy.Set(typeof(NestedAggregateHostedService).FullName, TenantHostedServiceDisposition.PerTenantInitializer);

            AggregateException exception =
                await Assert.ThrowsAsync<AggregateException>(() => harness.CreateAsync(Alpha).AsTask());

            Assert.Equal(new[] { "start:first", "start:second", "stop:first" }, events);
            Assert.Collection(
                exception.InnerExceptions,
                failure => Assert.Same(startupLeaf1, failure),
                failure => Assert.Same(startupLeaf2, failure),
                failure => Assert.Same(startupEmptyAggregate, failure),
                failure => Assert.Same(startupLeaf3, failure),
                failure => Assert.Same(startupLeaf4, failure),
                failure => Assert.Same(cleanupLeaf1, failure),
                failure => Assert.Same(cleanupLeaf2, failure),
                failure => Assert.Same(cleanupEmptyAggregate, failure),
                failure => Assert.Same(cleanupLeaf3, failure),
                failure => Assert.Same(cleanupLeaf4, failure));
        }

        private static T Resolve<T>(ITenantContainer container)
        {
            Assert.True(container.TryAcquire(out ITenantLease lease));
            using (lease)
            {
                return lease.Services.GetRequiredService<T>();
            }
        }

        private sealed class Harness
        {
            private ServiceProvider _rootProvider;

            public IServiceCollection RootServices { get; } = new ServiceCollection();

            public TenantSharedServiceRegistry SharedRegistry { get; } = new();

            public TenantHostedServicePolicy Policy { get; } = new();

            public List<ITenantServiceConfigurator> Configurators { get; } = new();

            public ServiceProvider RootProvider => _rootProvider ??= Build();

            public ValueTask<ITenantContainer> CreateAsync(TenantDescriptor tenant)
                => CreateAsync(tenant, CancellationToken.None);

            public ValueTask<ITenantContainer> CreateAsync(
                TenantDescriptor tenant,
                CancellationToken cancellationToken)
            {
                ServiceProvider root = RootProvider;
                var factory = root.GetRequiredService<ITenantContainerFactory>();
                return factory.CreateAsync(tenant, cancellationToken);
            }

            private ServiceProvider Build()
            {
                RootServices.AddSingleton(SharedRegistry);
                RootServices.AddSingleton<ITenantHostedServicePolicy>(Policy);
                RootServices.AddSingleton<ITenantServiceBlueprint>(new TenantServiceBlueprint(RootServices));
                RootServices.AddSingleton(TimeProvider.System);
                RootServices.AddSingleton<ITenantContainerFactory, TenantContainerFactory>();

                foreach (ITenantServiceConfigurator configurator in Configurators)
                {
                    RootServices.AddSingleton(configurator);
                }

                return RootServices.BuildServiceProvider(
                    new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false });
            }
        }

        private sealed class PerTenantService
        {
        }

        private sealed class ScopedDependency
        {
        }

        private sealed class SingletonCapturingScopedDependency
        {
            public SingletonCapturingScopedDependency(ScopedDependency scopedDependency)
            {
            }
        }

        private sealed class SharedService
        {
        }

        private sealed class SharedDisposable : IDisposable
        {
            public bool IsDisposed { get; private set; }

            public void Dispose() => IsDisposed = true;
        }

        private sealed class TenantOwnedDisposable : IDisposable
        {
            public bool IsDisposed { get; private set; }

            public void Dispose() => IsDisposed = true;
        }

        private sealed class StampedService
        {
            public StampedService(string stamp) => Stamp = stamp;

            public string Stamp { get; }
        }

        private sealed class StampingConfigurator : ITenantServiceConfigurator
        {
            public void Configure(IServiceCollection services, TenantDescriptor tenant)
            {
                services.RemoveAll<StampedService>();
                services.AddSingleton(new StampedService(tenant.TenantId.ToString()));
            }
        }

        private sealed class TenantContextAccessorProbe : ITenantContextAccessor
        {
            public TenantId Current { get; private set; }

            public void SetCurrent(TenantId tenantId) => Current = tenantId;
        }

        private sealed class AmbientTenantLifecycleProbe
        {
            public IList<ITenantContextAccessor> InjectedAccessors { get; } = new List<ITenantContextAccessor>();

            public IList<(string Phase, TenantId TenantId)> Observations { get; } =
                new List<(string Phase, TenantId TenantId)>();

            public ITenantContextAccessor ConfiguratorRegistration { get; set; }

            public InvalidOperationException StartupFailure { get; } = new("startup failed");

            public void Observe(string phase, ITenantContextAccessor accessor)
            {
                InjectedAccessors.Add(accessor);
                Observations.Add((phase, accessor.Current));
            }
        }

        private sealed class AmbientTenantConfigurator : ITenantServiceConfigurator
        {
            private readonly ITenantContextAccessor _accessor;
            private readonly AmbientTenantLifecycleProbe _probe;
            private readonly Exception _failure;

            public AmbientTenantConfigurator(
                ITenantContextAccessor accessor,
                AmbientTenantLifecycleProbe probe,
                Exception failure = null)
            {
                _accessor = accessor;
                _probe = probe;
                _failure = failure;
            }

            public void Configure(IServiceCollection services, TenantDescriptor tenant)
            {
                _probe.ConfiguratorRegistration = services
                    .Single(descriptor => descriptor.ServiceType == typeof(ITenantContextAccessor))
                    .ImplementationInstance as ITenantContextAccessor;
                _probe.Observe("configure", _accessor);

                if (_failure is not null)
                {
                    throw _failure;
                }
            }
        }

        private sealed class AmbientTenantInitializer : IHostedService
        {
            private readonly ITenantContextAccessor _accessor;
            private readonly AmbientTenantLifecycleProbe _probe;

            public AmbientTenantInitializer(
                ITenantContextAccessor accessor,
                AmbientTenantLifecycleProbe probe)
            {
                _accessor = accessor;
                _probe = probe;
                _probe.Observe("construct:success", _accessor);
            }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                _probe.Observe("start:success", _accessor);
                return Task.CompletedTask;
            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                _probe.Observe("stop:success", _accessor);
                return Task.CompletedTask;
            }
        }

        private sealed class AmbientTenantCancellationInitializer : IHostedService
        {
            private readonly ITenantContextAccessor _accessor;
            private readonly AmbientTenantLifecycleProbe _probe;

            public AmbientTenantCancellationInitializer(
                ITenantContextAccessor accessor,
                AmbientTenantLifecycleProbe probe)
            {
                _accessor = accessor;
                _probe = probe;
                _probe.Observe("construct:canceled", _accessor);
            }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                _probe.Observe("start:canceled", _accessor);
                return Task.FromCanceled(cancellationToken);
            }

            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }

        private sealed class AmbientTenantStartupFailureInitializer : IHostedService
        {
            private readonly ITenantContextAccessor _accessor;
            private readonly AmbientTenantLifecycleProbe _probe;

            public AmbientTenantStartupFailureInitializer(
                ITenantContextAccessor accessor,
                AmbientTenantLifecycleProbe probe)
            {
                _accessor = accessor;
                _probe = probe;
                _probe.Observe("construct:failure", _accessor);
            }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                _probe.Observe("start:failure", _accessor);
                throw _probe.StartupFailure;
            }

            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }

        private sealed class AmbientTenantStopCancellationInitializer : IHostedService
        {
            private readonly ITenantContextAccessor _accessor;
            private readonly AmbientTenantLifecycleProbe _probe;

            public AmbientTenantStopCancellationInitializer(
                ITenantContextAccessor accessor,
                AmbientTenantLifecycleProbe probe)
            {
                _accessor = accessor;
                _probe = probe;
            }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                _probe.Observe("start:stop-cancellation", _accessor);
                return Task.CompletedTask;
            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                _probe.Observe("stop:canceled", _accessor);
                return Task.FromCanceled(new CancellationToken(canceled: true));
            }
        }

        private sealed class ForwardingProbeConfigurator : ITenantServiceConfigurator
        {
            public object ForwardedAccessor { get; private set; }

            public void Configure(IServiceCollection services, TenantDescriptor tenant)
            {
                ForwardedAccessor = services
                    .Single(descriptor => descriptor.ServiceType == typeof(ITenantContextAccessor))
                    .ImplementationInstance;
            }
        }

        private sealed class InitializerHostedService : IHostedService
        {
            public bool Started { get; private set; }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                Started = true;
                return Task.CompletedTask;
            }

            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }

        private sealed class ScopeCapturingInitializerHostedService : IHostedService
        {
            public ScopeCapturingInitializerHostedService(SingletonCapturingScopedDependency dependency)
            {
            }

            public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }

        private sealed class FactoryHostedServiceTracker
        {
            private int _initializerStartCount;
            private int _sharedStartCount;

            public int InitializerStartCount => Volatile.Read(ref _initializerStartCount);

            public int SharedStartCount => Volatile.Read(ref _sharedStartCount);

            public void RecordInitializerStart() => Interlocked.Increment(ref _initializerStartCount);

            public void RecordSharedStart() => Interlocked.Increment(ref _sharedStartCount);
        }

        private sealed class FactoryBasedInitializerHostedService : IHostedService
        {
            private readonly FactoryHostedServiceTracker _tracker;

            public FactoryBasedInitializerHostedService(FactoryHostedServiceTracker tracker)
            {
                _tracker = tracker;
            }

            public bool Started { get; private set; }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                Started = true;
                _tracker.RecordInitializerStart();
                return Task.CompletedTask;
            }

            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }

        private sealed class FactoryBasedSharedHostedService : IHostedService
        {
            private readonly FactoryHostedServiceTracker _tracker;

            public FactoryBasedSharedHostedService(FactoryHostedServiceTracker tracker)
            {
                _tracker = tracker;
            }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                _tracker.RecordSharedStart();
                return Task.CompletedTask;
            }

            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }

        private sealed class RelocatedHostedService : IHostedService
        {
            public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }

        private sealed class SharedHostedService : IHostedService
        {
            public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }

        private sealed class OrderedSharedService
        {
            public OrderedSharedService(string name)
            {
                Name = name;
            }

            public string Name { get; }
        }

        private sealed class UnclassifiedHostedService : IHostedService
        {
            public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }

        private sealed class FailurePlan
        {
            private int _startupServiceDisposeCount;

            public InvalidOperationException DisposeFailure { get; } = new("dispose failed");

            public InvalidOperationException StartupFailure { get; } = new("startup failed");

            public int StartupServiceDisposeCount => Volatile.Read(ref _startupServiceDisposeCount);

            public InvalidOperationException StopFailure { get; } = new("stop failed");

            public void RecordStartupServiceDispose() => Interlocked.Increment(ref _startupServiceDisposeCount);
        }

        private sealed class CleanupFailureHostedService : IHostedService
        {
            private readonly FailurePlan _failures;

            public CleanupFailureHostedService(FailurePlan failures, ThrowingCleanupDisposable disposable)
            {
                _failures = failures;
            }

            public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            public Task StopAsync(CancellationToken cancellationToken) => throw _failures.StopFailure;
        }

        private sealed class StartupFailureHostedService : IHostedService, IDisposable
        {
            private readonly FailurePlan _failures;

            public StartupFailureHostedService(FailurePlan failures)
            {
                _failures = failures;
            }

            public Task StartAsync(CancellationToken cancellationToken) => throw _failures.StartupFailure;

            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            public void Dispose() => _failures.RecordStartupServiceDispose();
        }

        private sealed class ThrowingCleanupDisposable : IDisposable
        {
            private readonly FailurePlan _failures;

            public ThrowingCleanupDisposable(FailurePlan failures)
            {
                _failures = failures;
            }

            public void Dispose() => throw _failures.DisposeFailure;
        }

        private sealed class EmptyAggregateStopHostedService : IHostedService
        {
            private int _stopCallCount;

            public AggregateException Failure { get; } = new();

            public int StopCallCount => Volatile.Read(ref _stopCallCount);

            public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            public Task StopAsync(CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _stopCallCount);
                throw Failure;
            }
        }

        private sealed class NestedAggregateHostedService : IHostedService
        {
            private readonly string _name;
            private readonly IList<string> _events;
            private readonly AggregateException _startFailure;
            private readonly AggregateException _stopFailure;

            public NestedAggregateHostedService(
                string name,
                IList<string> events,
                AggregateException startFailure,
                AggregateException stopFailure)
            {
                _name = name;
                _events = events;
                _startFailure = startFailure;
                _stopFailure = stopFailure;
            }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                _events.Add($"start:{_name}");

                if (_startFailure is not null)
                {
                    throw _startFailure;
                }

                return Task.CompletedTask;
            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                _events.Add($"stop:{_name}");

                if (_stopFailure is not null)
                {
                    throw _stopFailure;
                }

                return Task.CompletedTask;
            }
        }

        private sealed class RecordingConfigurator : ITenantServiceConfigurator
        {
            private readonly string _name;
            private readonly IList<string> _events;
            private readonly OrderedSharedService _expectedShared;
            private readonly string _requiredPriorEvent;

            public RecordingConfigurator(
                string name,
                IList<string> events,
                OrderedSharedService expectedShared,
                string requiredPriorEvent)
            {
                _name = name;
                _events = events;
                _expectedShared = expectedShared;
                _requiredPriorEvent = requiredPriorEvent;
            }

            public void Configure(IServiceCollection services, TenantDescriptor tenant)
            {
                Assert.Same(
                    _expectedShared,
                    services.Single(descriptor => descriptor.ServiceType == typeof(OrderedSharedService)).ImplementationInstance);

                if (_requiredPriorEvent is not null)
                {
                    Assert.Contains(_requiredPriorEvent, _events);
                }

                _events.Add(_name);
            }
        }
    }
}
