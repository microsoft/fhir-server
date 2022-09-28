// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.SearchValues
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
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
        [InlineData("testing", "testing")]
        [InlineData(@"t\e|s$t,i|\ng", @"t\e|s$t,i|\ng")]
        [InlineData(@"a\\b\,c\$d\|", @"a\b,c$d|")]
        public void GivenAString_WhenParseStringIntoSearchValue_ThenStringValueGotUnescaped(string data, string expected)
        {
            StringSearchValue value = StringSearchValue.Parse(data);
            Assert.Equal(expected, value.String);
        }

        [Theory]
        [InlineData("testing", "testing")]
        [InlineData(@"t\e|s$t,i|\ng", @"t\e|s$t,i|\ng")]
        [InlineData(@"a\\b\,c\$d\|", @"a\b,c$d|")]
        public void GivenAString_WhenSearchValueCreated_ThenStringValueGotUnescaped(string data, string expected)
        {
            StringSearchValue value = new StringSearchValue(data);
            Assert.Equal(expected, value.String);
        }

        [Theory]
        [InlineData(@"testing", "testing")]
        [InlineData(@"t\e|s$t,i|\ng", @"t\\e\|s\$t\,i\|\\ng")]
        [InlineData(@"a\\b\,c\$d\|", @"a\\b\,c\$d\|")]
        public void GivenASearchValue_WhenToStringIsCalled_ThenEscapedStringShouldBeReturned(string s, string expected)
        {
            StringSearchValue value = new StringSearchValue(s);

            Assert.Equal(expected, value.ToString());
        }

        [Theory]
        [InlineData("Country", "country", 0)]
        [InlineData("Country", "city", 1)]
        [InlineData("123433", "798012", -1)]
        [InlineData("Muller", "Müller", 0)]
        public void GivenASearchValue_WhenCompareWithStringSearchValue_ThenCorrectResultIsReturned(string original, string given, int expectedResult)
        {
            StringSearchValue originalValue = new StringSearchValue(original);
            StringSearchValue givenValue = new StringSearchValue(given);

            int result = originalValue.CompareTo(givenValue, ComparisonRange.Max);

            Assert.Equal(expectedResult, result);
        }

        [Fact]
        public void GivenAStringSearchValue_WhenCompareWithNull_ThenArgumentExceptionIsThrown()
        {
            StringSearchValue value = new StringSearchValue("string");

            Assert.Throws<ArgumentException>(() => value.CompareTo(null, ComparisonRange.Max));
        }
    }
}
