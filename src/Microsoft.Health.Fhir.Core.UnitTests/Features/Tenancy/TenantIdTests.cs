// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Tenancy;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Tenancy
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class TenantIdTests
    {
        [Fact]
        public void GivenTheDefaultTenantId_WhenInspected_ThenItHasTheWellKnownValue()
        {
            Assert.Equal("(default)", TenantId.Default.Value);
            Assert.Equal("(default)", TenantId.Default.ToString());
        }

        [Fact]
        public void GivenADefaultConstructedTenantId_WhenValueIsRead_ThenItEqualsDefault()
        {
            TenantId uninitialized = default;

            Assert.Equal(TenantId.Default, uninitialized);
            Assert.Equal("(default)", uninitialized.Value);
        }

        [Theory]
        [InlineData("contoso")]
        [InlineData("a")]
        [InlineData("tenant-with-dashes-01")]
        public void GivenAValidName_WhenConstructed_ThenValueIsPreserved(string name)
        {
            var tenantId = new TenantId(name);

            Assert.Equal(name, tenantId.Value);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void GivenABlankName_WhenConstructed_ThenArgumentExceptionIsThrown(string name)
        {
            Assert.ThrowsAny<ArgumentException>(() => new TenantId(name));
        }

        [Fact]
        public void GivenTwoTenantIdsDifferingOnlyByCase_WhenCompared_ThenTheyAreEqual()
        {
            var lower = new TenantId("contoso");
            var upper = new TenantId("CONTOSO");

            Assert.Equal(lower, upper);
            Assert.True(lower == upper);
            Assert.Equal(lower.GetHashCode(), upper.GetHashCode());
        }

        [Fact]
        public void GivenTwoDifferentTenantIds_WhenCompared_ThenTheyAreNotEqual()
        {
            var first = new TenantId("contoso");
            var second = new TenantId("fabrikam");

            Assert.NotEqual(first, second);
            Assert.True(first != second);
        }
    }
}
