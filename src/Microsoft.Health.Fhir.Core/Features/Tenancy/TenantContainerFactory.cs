// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Health.Fhir.Core.Features.Tenancy
{
    /// <summary>
    /// Default <see cref="ITenantContainerFactory"/>. Builds each tenant container by cloning the root
    /// service registrations, replacing the small set of process-wide services with instances forwarded
    /// from the root provider, filtering hosted services by policy, and applying tenant configurators.
    /// </summary>
    public sealed class TenantContainerFactory : ITenantContainerFactory
    {
        /// <summary>
        /// Services that implement tenancy itself. These must be forwarded from the root rather than
        /// rebuilt, otherwise each tenant container would contain its own container factory.
        /// </summary>
        private static readonly Type[] TenancyInfrastructureTypes =
        {
            typeof(ITenantServiceBlueprint),
            typeof(TenantSharedServiceRegistry),
            typeof(ITenantHostedServicePolicy),
            typeof(ITenantContainerFactory),
            typeof(ITenantServiceConfigurator),
        };

        private readonly IServiceProvider _rootProvider;
        private readonly ITenantServiceBlueprint _blueprint;
        private readonly TenantSharedServiceRegistry _sharedServices;
        private readonly ITenantHostedServicePolicy _hostedServicePolicy;
        private readonly IEnumerable<ITenantServiceConfigurator> _configurators;
        private readonly TimeProvider _timeProvider;
        private readonly Lazy<IReadOnlyList<string>> _rootHostedServiceTypeNamesInRegistrationOrder;

        /// <summary>
        /// Initializes a new instance of the <see cref="TenantContainerFactory"/> class.
        /// </summary>
        /// <param name="rootProvider">The root service provider.</param>
        /// <param name="blueprint">The captured root service registrations.</param>
        /// <param name="sharedServices">The services shared with every tenant.</param>
        /// <param name="hostedServicePolicy">The hosted service classification policy.</param>
        /// <param name="configurators">Tenant-specific configuration steps.</param>
        /// <param name="timeProvider">The time provider used for idle tracking.</param>
        public TenantContainerFactory(
            IServiceProvider rootProvider,
            ITenantServiceBlueprint blueprint,
            TenantSharedServiceRegistry sharedServices,
            ITenantHostedServicePolicy hostedServicePolicy,
            IEnumerable<ITenantServiceConfigurator> configurators,
            TimeProvider timeProvider)
        {
            EnsureArg.IsNotNull(rootProvider, nameof(rootProvider));
            EnsureArg.IsNotNull(blueprint, nameof(blueprint));
            EnsureArg.IsNotNull(sharedServices, nameof(sharedServices));
            EnsureArg.IsNotNull(hostedServicePolicy, nameof(hostedServicePolicy));
            EnsureArg.IsNotNull(configurators, nameof(configurators));
            EnsureArg.IsNotNull(timeProvider, nameof(timeProvider));

            _rootProvider = rootProvider;
            _blueprint = blueprint;
            _sharedServices = sharedServices;
            _hostedServicePolicy = hostedServicePolicy;
            _configurators = configurators;
            _timeProvider = timeProvider;

            // Resolving hosted services in the constructor could re-enter the container while the host is
            // starting. Tenant construction occurs after host startup, so resolve them only when first needed.
            _rootHostedServiceTypeNamesInRegistrationOrder = new Lazy<IReadOnlyList<string>>(
                () => _rootProvider.GetServices<IHostedService>().Select(service => service.GetType().FullName).ToArray(),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <inheritdoc />
        public async ValueTask<ITenantContainer> CreateAsync(
            TenantDescriptor tenant,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(tenant, nameof(tenant));

            var tenantServices = new ServiceCollection();

            foreach (ServiceDescriptor descriptor in _blueprint.CreateSnapshot())
            {
                tenantServices.Add(descriptor);
            }

            ForwardTenancyInfrastructure(tenantServices);
            ForwardSharedServices(tenantServices);
            FilterHostedServices(tenantServices);

            foreach (ITenantServiceConfigurator configurator in _configurators)
            {
                configurator.Configure(tenantServices, tenant);
            }

            ServiceProvider provider = tenantServices.BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false });

            var container = new TenantContainer(tenant, provider, _timeProvider);

            try
            {
                await container.StartInitializersAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // TenantContainer disposal preserves a lone startup failure and aggregates it with any
                // initializer-stop or provider-disposal failures.
                await container.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            return container;
        }

        private void ForwardTenancyInfrastructure(ServiceCollection tenantServices)
        {
            foreach (Type type in TenancyInfrastructureTypes)
            {
                Forward(tenantServices, type);
            }

            Forward(tenantServices, typeof(ITenantContextAccessor));
            Forward(tenantServices, typeof(ITenantRegistry));
        }

        private void ForwardSharedServices(ServiceCollection tenantServices)
        {
            foreach (Type type in _sharedServices.SharedServiceTypes)
            {
                Forward(tenantServices, type);
            }
        }

        private void Forward(ServiceCollection tenantServices, Type serviceType)
        {
            bool wasRegistered = tenantServices.Any(descriptor => descriptor.ServiceType == serviceType);

            tenantServices.RemoveAll(serviceType);

            if (!wasRegistered)
            {
                return;
            }

            foreach (object instance in (IEnumerable<object>)_rootProvider.GetServices(serviceType))
            {
                if (instance != null)
                {
                    tenantServices.Add(new ServiceDescriptor(serviceType, instance));
                }
            }
        }

        private void FilterHostedServices(ServiceCollection tenantServices)
        {
            List<ServiceDescriptor> hostedDescriptors = tenantServices
                .Where(descriptor => descriptor.ServiceType == typeof(IHostedService))
                .ToList();

            if (hostedDescriptors.Count == 0)
            {
                return;
            }

            IReadOnlyList<string> rootTypeNames = _rootHostedServiceTypeNamesInRegistrationOrder.Value;

            for (int descriptorIndex = 0; descriptorIndex < hostedDescriptors.Count; descriptorIndex++)
            {
                ServiceDescriptor descriptor = hostedDescriptors[descriptorIndex];

                string typeName = descriptor.ImplementationType?.FullName;

                if (typeName == null)
                {
                    typeName = descriptor.ImplementationInstance?.GetType().FullName;
                }

                // Factory registrations do not expose their implementation type. Root services resolve in
                // registration order, so the matching resolved instance is at the descriptor's position.
                if (typeName == null && descriptorIndex < rootTypeNames.Count)
                {
                    typeName = rootTypeNames[descriptorIndex];
                }

                if (typeName == null)
                {
                    throw new TenantHostedServiceNotClassifiedException(
                        $"IHostedService descriptor at index {descriptorIndex}");
                }

                if (_hostedServicePolicy.Classify(typeName) != TenantHostedServiceDisposition.PerTenantInitializer)
                {
                    tenantServices.Remove(descriptor);
                }
            }
        }
    }
}
