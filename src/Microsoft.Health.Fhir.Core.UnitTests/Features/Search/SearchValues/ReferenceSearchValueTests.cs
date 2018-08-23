// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.SearchValues
{
    public class ReferenceSearchValueTests
    {
        private const string ParamNameReference = "reference";
        private const string ParamNameS = "s";

        [Fact]
        public void GivenANullReference_WhenInitializing_ThenExceptionShouldBeThrown()
        {
            Assert.Throws<ArgumentNullException>(ParamNameReference, () => new ReferenceSearchValue(null));
        }

        [Theory]
        [InlineData("")]
        [InlineData("    ")]
        public void GivenAnInvalidReference_WhenInitializing_ThenExceptionShouldBeThrown(string s)
        {
            Assert.Throws<ArgumentException>(ParamNameReference, () => new ReferenceSearchValue(s));
        }

        [Fact]
        public void GivenANullString_WhenParsing_ThenExceptionShouldBeThrown()
        {
            Assert.Throws<ArgumentNullException>(ParamNameS, () => ReferenceSearchValue.Parse(null));
        }

        [Theory]
        [InlineData("")]
        [InlineData("    ")]
        public void GivenAnInvalidString_WhenParsing_ThenExceptionShouldBeThrown(string s)
        {
            Assert.Throws<ArgumentException>(ParamNameS, () => ReferenceSearchValue.Parse(s));
        }

        [Fact]
        public void GivenAValidString_WhenParsed_ThenCorrectSearchValueShouldBeReturned()
        {
            string expected = "Observation/abc";

            ReferenceSearchValue value = ReferenceSearchValue.Parse(expected);

            Assert.NotNull(value);
            Assert.Equal(expected, value.Reference);
        }

        [Fact]
        public void GivenASearchValue_WhenToStringIsCalled_ThenCorrectStringShouldBeReturned()
        {
            string expected = "Organization/1";

            ReferenceSearchValue value = new ReferenceSearchValue(expected);

            Assert.Equal(expected, value.ToString());
        }
    }
}
