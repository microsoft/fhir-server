// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Persistence
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class WeakETagTests
    {
        [Fact]
        public void GivenAWeakETag_WhenRemovingETagDecoration_ThenJustVersionShouldRemain()
        {
            var weakETag = WeakETag.FromWeakETag("W/\"version1\"");

            Assert.Equal("version1", weakETag.VersionId);
        }

        [Fact]
        public void GivenANonWeakETag_WhenRemovingETagDecoration_ThenOriginalShouldRemain()
        {
            var weakETag = WeakETag.FromVersionId("\"version1\"");

            Assert.Equal("\"version1\"", weakETag.VersionId);
        }

        [Fact]
        public void GivenAWeakETag_WhenUsingTheWrongMethodToCreate_ThenThrow()
        {
            Assert.Throws<ArgumentException>(() => WeakETag.FromVersionId("W/\"version1\""));
        }

        [Fact]
        public void GivenANonWeakETag_WhenUsingTheWrongMethodToCreate_ThenThrow()
        {
            Assert.Throws<BadRequestException>(() => WeakETag.FromWeakETag("\"version1\""));
        }

        [Fact]
        public void GivenAVersion_WhenAddingETagDecoration_AWeakEtagShouldBeReturned()
        {
            var weakETag = WeakETag.FromVersionId("version1");

            Assert.Equal("W/\"version1\"", weakETag.ToString());
        }

        [Fact]
        public void GivenAWeakETagString_WhenAddingETagDecoration_AWeakEtagShouldBeReturned()
        {
            var weakETag = WeakETag.FromWeakETag("W/\"version1\"");

            Assert.Equal("W/\"version1\"", weakETag.ToString());
        }
    }
}
