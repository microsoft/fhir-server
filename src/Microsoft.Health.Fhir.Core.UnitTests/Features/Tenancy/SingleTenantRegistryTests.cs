// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Tenancy;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Tenancy
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class SingleTenantRegistryTests
    {
        private readonly SingleTenantRegistry _registry = new();

        [Fact]
        public void GivenTheSingleTenantRegistry_WhenTenantsIsRead_ThenExactlyTheDefaultTenantIsReturned()
        {
            Assert.Single(_registry.Tenants);
            Assert.Equal(TenantId.Default, _registry.Tenants.Single().TenantId);
        }

        [Fact]
        public void GivenTheSingleTenantRegistry_WhenTheDefaultTenantIsRequested_ThenItIsFound()
        {
            bool found = _registry.TryGetTenant(TenantId.Default, out TenantDescriptor descriptor);

            Assert.True(found);
            Assert.NotNull(descriptor);
            Assert.Equal(TenantId.Default, descriptor.TenantId);
            Assert.Empty(descriptor.Properties);
        }

        [Fact]
        public void GivenTheSingleTenantRegistry_WhenAnUnknownTenantIsRequested_ThenItIsNotFound()
        {
            bool found = _registry.TryGetTenant(new TenantId("contoso"), out TenantDescriptor descriptor);

            Assert.False(found);
            Assert.Null(descriptor);
        }

        [Fact]
        public void GivenATenantDescriptorWithProperties_WhenPropertiesAreRead_ThenTheyAreExposedReadOnly()
        {
            var sourceProperties = new Dictionary<string, string> { { "pool", "fhir-eus-01" } };

            var descriptor = new TenantDescriptor(
                new TenantId("contoso"),
                new Uri("https://contoso.fhir.azurehealthcareapis.com/"),
                sourceProperties);

            Assert.Equal(new TenantId("contoso"), descriptor.TenantId);
            Assert.Equal("https://contoso.fhir.azurehealthcareapis.com/", descriptor.BaseUri!.ToString());
            Assert.Equal("fhir-eus-01", descriptor.Properties["pool"]);
        }

        [Fact]
        public void GivenATenantDescriptorWithProperties_WhenPropertiesAreLookedUpUsingDifferentCasing_ThenTheyAreCaseInsensitive()
        {
            var descriptor = new TenantDescriptor(
                new TenantId("contoso"),
                new Uri("https://contoso.fhir.azurehealthcareapis.com/"),
                new Dictionary<string, string> { { "pool", "fhir-eus-01" } });

            Assert.Equal("fhir-eus-01", descriptor.Properties["POOL"]);
            Assert.True(descriptor.Properties.ContainsKey("PoOl"));
        }

        [Fact]
        public void GivenATenantDescriptorWithProperties_WhenTheSourceDictionaryChanges_ThenTheDescriptorIsDefensivelyCopied()
        {
            var sourceProperties = new Dictionary<string, string> { { "pool", "fhir-eus-01" } };

            var descriptor = new TenantDescriptor(
                new TenantId("contoso"),
                new Uri("https://contoso.fhir.azurehealthcareapis.com/"),
                sourceProperties);

            sourceProperties["pool"] = "changed";
            sourceProperties["region"] = "eastus";

            Assert.Equal("fhir-eus-01", descriptor.Properties["pool"]);
            Assert.False(descriptor.Properties.ContainsKey("region"));
        }

        [Fact]
        public void GivenATenantDescriptorWithProperties_WhenMutationIsAttempted_ThenThePropertiesAreReadOnly()
        {
            var descriptor = new TenantDescriptor(
                new TenantId("contoso"),
                new Uri("https://contoso.fhir.azurehealthcareapis.com/"),
                new Dictionary<string, string> { { "pool", "fhir-eus-01" } });

            IDictionary<string, string> properties = Assert.IsAssignableFrom<IDictionary<string, string>>(descriptor.Properties);

            Assert.True(properties.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => properties.Add("region", "eastus"));
        }
    }
}
