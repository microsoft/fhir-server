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
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    { "FhirServer:Security:Enabled", "false" },
                })
                .Build();

            _services.AddFhirServer(configuration);
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
    }
}
