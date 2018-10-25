// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.SearchValues
{
    public class CompositeSearchValueTests
    {
        private const string ParamNameComponents = "components";

        [Fact]
        public void GivenANullComponents_WhenInitializing_ThenExceptionShouldBeThrown()
        {
            Assert.Throws<ArgumentNullException>(ParamNameComponents, () => new CompositeSearchValue(null));
        }

        [Fact]
        public void GivenAnEmptyComponents_WhenInitializing_ThenExceptionShouldBeThrown()
        {
            Assert.Throws<ArgumentException>(ParamNameComponents, () => new CompositeSearchValue(new StringSearchValue[0]));
        }

        [Fact]
        public void GivenComponents_WhenInitialized_ThenCorrectComponentsShouldBeAssigned()
        {
            var components = new ISearchValue[]
            {
                new StringSearchValue("abc"),
                new NumberSearchValue(123),
            };

            var value = new CompositeSearchValue(components);

            Assert.Same(components, value.Components);
        }

        [Fact]
        public void GivenASearchValue_WhenIsValidCompositeComponentIsCalled_ThenFalseShouldBeReturned()
        {
            var components = new ISearchValue[]
            {
                new StringSearchValue("abc"),
            };

            var value = new CompositeSearchValue(components);

            Assert.False(value.IsValidAsCompositeComponent);
        }

        [Fact]
        public void GivenASearchValue_WhenToStringIsCalled_ThenCorrectStringShouldBeReturned()
        {
            var components = new ISearchValue[]
            {
                new TokenSearchValue("system", "code", "text"),
                new NumberSearchValue(123),
            };

            var value = new CompositeSearchValue(components);

            Assert.Equal("system|code$123", value.ToString());
        }
    }
}
