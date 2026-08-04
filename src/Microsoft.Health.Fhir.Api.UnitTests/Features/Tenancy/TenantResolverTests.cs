// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.Health.Fhir.Api.Features.Tenancy;
using Microsoft.Health.Fhir.Core.Features.Tenancy;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Tenancy
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class TenantResolverTests
    {
        [Fact]
        public void GivenASingleTenantResolver_WhenResolving_ThenTheDefaultTenantIsReturned()
        {
            ITenantResolver resolver = new SingleTenantResolver();

            var context = new DefaultHttpContext();
            context.Request.Host = new HostString("anything.example.org");

            Assert.True(resolver.TryResolve(context, out TenantId tenantId));
            Assert.Equal(TenantId.Default, tenantId);
        }

        [Fact]
        public void GivenAHostHeaderResolver_WhenTheHostMatchesATenant_ThenThatTenantIsReturned()
        {
            var registry = new StaticTenantRegistry(
                new TenantDescriptor(new TenantId("alpha"), new Uri("https://alpha.example.org")),
                new TenantDescriptor(new TenantId("beta"), new Uri("https://beta.example.org")));

            ITenantResolver resolver = new HostHeaderTenantResolver(registry);

            var context = new DefaultHttpContext();
            context.Request.Host = new HostString("beta.example.org");

            Assert.True(resolver.TryResolve(context, out TenantId tenantId));
            Assert.Equal(new TenantId("beta"), tenantId);
        }

        [Fact]
        public void GivenAHostHeaderResolver_WhenTheHostIsUnknown_ThenResolutionFails()
        {
            var registry = new StaticTenantRegistry(
                new TenantDescriptor(new TenantId("alpha"), new Uri("https://alpha.example.org")));

            ITenantResolver resolver = new HostHeaderTenantResolver(registry);

            var context = new DefaultHttpContext();
            context.Request.Host = new HostString("gamma.example.org");

            Assert.False(resolver.TryResolve(context, out TenantId tenantId));
            Assert.Equal(default, tenantId);
        }

        [Fact]
        public void GivenAHostHeaderResolver_WhenThePortDiffers_ThenTheTenantStillMatches()
        {
            var registry = new StaticTenantRegistry(
                new TenantDescriptor(new TenantId("alpha"), new Uri("https://alpha.example.org")));

            ITenantResolver resolver = new HostHeaderTenantResolver(registry);

            var context = new DefaultHttpContext();
            context.Request.Host = new HostString("alpha.example.org", 44300);

            Assert.True(resolver.TryResolve(context, out TenantId tenantId));
            Assert.Equal(new TenantId("alpha"), tenantId);
        }

        [Fact]
        public void GivenAHostHeaderResolver_WhenTheHostCasingDiffers_ThenTheTenantStillMatches()
        {
            var registry = new StaticTenantRegistry(
                new TenantDescriptor(new TenantId("alpha"), new Uri("https://alpha.example.org")));

            ITenantResolver resolver = new HostHeaderTenantResolver(registry);

            var context = new DefaultHttpContext();
            context.Request.Host = new HostString("ALPHA.Example.ORG");

            Assert.True(resolver.TryResolve(context, out TenantId tenantId));
            Assert.Equal(new TenantId("alpha"), tenantId);
        }

        private sealed class StaticTenantRegistry : ITenantRegistry
        {
            private readonly Dictionary<TenantId, TenantDescriptor> _tenants = new();

            public StaticTenantRegistry(params TenantDescriptor[] tenants)
            {
                foreach (TenantDescriptor tenant in tenants)
                {
                    _tenants[tenant.TenantId] = tenant;
                }
            }

            public IReadOnlyCollection<TenantDescriptor> Tenants => _tenants.Values;

            public bool TryGetTenant(TenantId tenantId, out TenantDescriptor descriptor)
                => _tenants.TryGetValue(tenantId, out descriptor);
        }
    }
}
