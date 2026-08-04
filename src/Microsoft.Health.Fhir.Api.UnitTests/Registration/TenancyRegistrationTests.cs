// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Tenancy;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Registration
{
    /// <summary>
    /// Asserts that <c>AddFhirServer</c> itself wires the single-tenant defaults. These assertions run
    /// against the descriptors <c>AddFhirServer</c> actually produced, so deleting the production
    /// registration turns this test red.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class TenancyRegistrationTests
    {
        private readonly IServiceCollection _services = new ServiceCollection();

        public TenancyRegistrationTests()
        {
            _services.AddFhirServer(CreateConfiguration());
        }

        [Fact]
        public void GivenAddFhirServer_WhenInspected_ThenTheAmbientTenantAccessorIsRegisteredOnceAsASingleton()
        {
            ServiceDescriptor descriptor = Assert.Single(
                _services,
                d => d.ServiceType == typeof(ITenantContextAccessor));

            Assert.Equal(typeof(TenantContextAccessor), descriptor.ImplementationType);
            Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        }

        [Fact]
        public void GivenAddFhirServer_WhenInspected_ThenTheSingleTenantRegistryIsRegisteredOnceAsASingleton()
        {
            ServiceDescriptor descriptor = Assert.Single(
                _services,
                d => d.ServiceType == typeof(ITenantRegistry));

            Assert.Equal(typeof(SingleTenantRegistry), descriptor.ImplementationType);
            Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        }

        [Fact]
        public void GivenCustomTenancySeamsRegisteredBeforeAddFhirServer_WhenInspected_ThenOnlyTheCustomDescriptorsRemain()
        {
            var services = new ServiceCollection();
            var customTenantContextAccessor = new TestTenantContextAccessor();
            var customTenantRegistry = new TestTenantRegistry();

            services.AddSingleton<ITenantContextAccessor>(customTenantContextAccessor);
            services.AddSingleton<ITenantRegistry>(customTenantRegistry);

            services.AddFhirServer(CreateConfiguration());

            ServiceDescriptor tenantContextAccessorDescriptor = Assert.Single(
                services,
                d => d.ServiceType == typeof(ITenantContextAccessor));
            ServiceDescriptor tenantRegistryDescriptor = Assert.Single(
                services,
                d => d.ServiceType == typeof(ITenantRegistry));

            Assert.Same(customTenantContextAccessor, tenantContextAccessorDescriptor.ImplementationInstance);
            Assert.Equal(ServiceLifetime.Singleton, tenantContextAccessorDescriptor.Lifetime);
            Assert.Same(customTenantRegistry, tenantRegistryDescriptor.ImplementationInstance);
            Assert.Equal(ServiceLifetime.Singleton, tenantRegistryDescriptor.Lifetime);
        }

        private static IConfiguration CreateConfiguration()
        {
            return new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    { "FhirServer:Security:Enabled", "false" },
                })
                .Build();
        }

        private sealed class TestTenantContextAccessor : ITenantContextAccessor
        {
            public TenantId Current => TenantId.Default;

            public void SetCurrent(TenantId tenantId)
            {
            }
        }

        private sealed class TestTenantRegistry : ITenantRegistry
        {
            public IReadOnlyCollection<TenantDescriptor> Tenants { get; } = new List<TenantDescriptor>();

            public bool TryGetTenant(TenantId tenantId, out TenantDescriptor descriptor)
            {
                descriptor = null;
                return false;
            }
        }
    }
}
