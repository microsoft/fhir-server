// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Tenancy;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Tenancy
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class TenantSharedServiceRegistryTests
    {
        [Fact]
        public void GivenAnEmptyRegistry_WhenQueried_ThenNothingIsShared()
        {
            var registry = new TenantSharedServiceRegistry();

            Assert.False(registry.IsShared(typeof(ILoggerFactory)));
            Assert.Empty(registry.SharedServiceTypes);
        }

        [Fact]
        public void GivenARegisteredType_WhenQueried_ThenItIsShared()
        {
            var registry = new TenantSharedServiceRegistry();
            registry.ShareWithTenants<ILoggerFactory>();

            Assert.True(registry.IsShared(typeof(ILoggerFactory)));
            Assert.Contains(typeof(ILoggerFactory), registry.SharedServiceTypes);
        }

        [Fact]
        public void GivenADuplicateRegistration_WhenQueried_ThenItAppearsOnce()
        {
            var registry = new TenantSharedServiceRegistry();
            registry.ShareWithTenants<ILoggerFactory>();
            registry.ShareWithTenants<ILoggerFactory>();

            Assert.Single(registry.SharedServiceTypes);
        }

        [Fact]
        public void GivenANullType_WhenShared_ThenAnArgumentExceptionIsThrown()
        {
            var registry = new TenantSharedServiceRegistry();

            Assert.Throws<ArgumentNullException>(() => registry.ShareWithTenants((Type)null));
        }
    }
}
