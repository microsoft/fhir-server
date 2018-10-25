// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.SearchValues
{
    public class StringSearchValueTests
    {
        private const string ParamNameS = "s";

        [Fact]
        public void GivenANullString_WhenInitializing_ThenExceptionShouldBeThrown()
        {
            Assert.Throws<ArgumentNullException>(ParamNameS, () => new StringSearchValue(null));
        }

        [Theory]
        [InlineData("")]
        [InlineData("    ")]
        public void GivenAnInvalidString_WhenInitializing_ThenExceptionShouldBeThrown(string s)
        {
            Assert.Throws<ArgumentException>(ParamNameS, () => new StringSearchValue(s));
        }

        [Fact]
        public void GivenANullString_WhenParsing_ThenExceptionShouldBeThrown()
        {
            Assert.Throws<ArgumentNullException>(ParamNameS, () => StringSearchValue.Parse(null));
        }

        [Theory]
        [InlineData("")]
        [InlineData("    ")]
        public void GivenAnInvalidString_WhenParsing_ThenExceptionShouldBeThrown(string s)
        {
            Assert.Throws<ArgumentException>(ParamNameS, () => StringSearchValue.Parse(s));
        }

        [Fact]
        public void GivenAValidString_WhenParsed_ThenCorrectSearchValueShouldBeReturned()
        {
            string expected = "test string";

            StringSearchValue value = StringSearchValue.Parse(expected);

            Assert.NotNull(value);
            Assert.Equal(expected, value.String);
        }

        [Fact]
        public void GivenASearchValue_WhenIsValidCompositeComponentIsCalled_ThenTrueShouldBeReturned()
        {
            var value = new StringSearchValue("test");

            Assert.True(value.IsValidAsCompositeComponent);
        }

        [Theory]
        [InlineData(@"testing", "testing")]
        [InlineData(@"t\e|s$t,i|\ng", @"t\\e\|s\$t\,i\|\\ng")]
        public void GiveASearchValue_WhenToStringIsCalled_ThenCorrectStringShouldBeReturned(string s, string expected)
        {
            StringSearchValue value = new StringSearchValue(s);

            Assert.Equal(expected, value.ToString());
        }
    }
}
