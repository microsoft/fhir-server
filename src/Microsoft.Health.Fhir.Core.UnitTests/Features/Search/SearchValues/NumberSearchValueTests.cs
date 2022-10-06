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
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class NumberSearchValueTests
    {
        private const string ParamNameS = "s";

        [Fact]
        public void GivenANullString_WhenParsing_ThenExceptionShouldBeThrown()
        {
            Assert.Throws<ArgumentNullException>(ParamNameS, () => NumberSearchValue.Parse(null));
        }

        [Theory]
        [InlineData("")]
        [InlineData("    ")]
        public void GivenAnInvalidString_WhenParsing_ThenExceptionShouldBeThrown(string s)
        {
            Assert.Throws<ArgumentException>(ParamNameS, () => NumberSearchValue.Parse(s));
        }

        [Fact]
        public void GivenAStringThatIsNotDecimalNumber_WhenParsing_ThenExceptionShouldBeThrown()
        {
            Assert.Throws<FormatException>(() => NumberSearchValue.Parse("abc"));
        }

        [Fact]
        public void GivenAStringThatOverflows_WhenParsing_ThenExceptionShouldBeThrown()
        {
            Assert.Throws<OverflowException>(() => NumberSearchValue.Parse("79228162514264337593543950336"));
        }

        [Fact]
        public void GivenAValidString_WhenParsed_ThenCorrectSearchValueShouldBeCreated()
        {
            NumberSearchValue value = NumberSearchValue.Parse("245234.34");

            Assert.NotNull(value);
            Assert.Equal(245234.34m, value.Low);
            Assert.Equal(value.Low, value.High);
        }

        [Fact]
        public void GivenAStringWithTrailingZero_WhenParsed_ThenTrailingZeroShouldBePreserved()
        {
            string expected = "0.010";

            NumberSearchValue value = NumberSearchValue.Parse(expected);

            Assert.NotNull(value);
            Assert.Equal(0.010m, value.Low);
            Assert.Equal(value.Low, value.High);
            Assert.Equal(expected, value.ToString());
        }

        [Fact]
        public void GivenASearchValue_WhenIsValidCompositeComponentIsCalled_ThenTrueShouldBeReturned()
        {
            var value = new NumberSearchValue(123);

            Assert.True(value.IsValidAsCompositeComponent);
        }

        [Fact]
        public void GivenASearchValueWithEqualLowAndHighValues_WhenToStringIsCalled_ThenCorrectStringShouldBeReturned()
        {
            NumberSearchValue value = new NumberSearchValue(23.56m);

            Assert.Equal("23.56", value.ToString());
        }

        [Fact]
        public void GivenASearchValueWithUnequalLowAndHighValues_WhenToStringIsCalled_ThenCorrectStringShouldBeReturned()
        {
            NumberSearchValue value = new NumberSearchValue(23.56m, 27);

            Assert.Equal("[23.56, 27)", value.ToString());
        }
    }
}
