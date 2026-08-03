// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Tenancy;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Tenancy
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class TenantContextAccessorTests
    {
        [Fact]
        public void GivenAFreshAccessor_WhenCurrentIsRead_ThenTheDefaultTenantIsReturned()
        {
            var accessor = new TenantContextAccessor();

            Assert.Equal(TenantId.Default, accessor.Current);
        }

        [Fact]
        public void GivenAnAccessor_WhenCurrentIsSet_ThenTheSameValueIsReadBack()
        {
            var accessor = new TenantContextAccessor();
            var tenantId = new TenantId("contoso");

            accessor.SetCurrent(tenantId);

            Assert.Equal(tenantId, accessor.Current);
        }

        [Fact]
        public async Task GivenAnAccessor_WhenCurrentIsSetBeforeAnAwait_ThenTheValueFlowsAcrossTheAwait()
        {
            var accessor = new TenantContextAccessor();
            accessor.SetCurrent(new TenantId("contoso"));

            await Task.Yield();

            Assert.Equal(new TenantId("contoso"), accessor.Current);
        }

        [Fact]
        public async Task GivenTwoConcurrentFlows_WhenEachSetsItsOwnTenant_ThenNeitherObservesTheOther()
        {
            var accessor = new TenantContextAccessor();

            async Task<TenantId> RunAsync(string name)
            {
                accessor.SetCurrent(new TenantId(name));
                await Task.Delay(25);
                return accessor.Current;
            }

            Task<TenantId> first = Task.Run(() => RunAsync("contoso"));
            Task<TenantId> second = Task.Run(() => RunAsync("fabrikam"));

            TenantId[] results = await Task.WhenAll(first, second);

            Assert.Equal(new TenantId("contoso"), results[0]);
            Assert.Equal(new TenantId("fabrikam"), results[1]);
            Assert.Equal(TenantId.Default, accessor.Current);
        }
    }
}
