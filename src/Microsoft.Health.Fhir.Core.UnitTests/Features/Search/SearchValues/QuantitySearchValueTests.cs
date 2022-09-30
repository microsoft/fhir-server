// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.SearchValues
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class QuantitySearchValueTests
    {
        private const string ParamNameS = "s";

        private const string DefaultSystem = "system";
        private const string DefaultCode = "code";
        private const decimal DefaultValue = 2.53m;

        private QuantitySearchValueBuilder _builder = new QuantitySearchValueBuilder();

        public static IEnumerable<object[]> GetSystemAndCode()
        {
            string[] entries = new string[] { null, string.Empty, "    " };

            foreach (string system in entries)
            {
                foreach (string code in entries)
                {
                    yield return new[] { system, code };
                }
            }
        }

        [Theory]
        [MemberData(nameof(GetSystemAndCode))]
        public void GivenAValue_WhenInitialized_ThenAnySystemAndCodeShouldBeAllowed(string system, string code)
        {
            _builder.System = system;
            _builder.Code = code;

            QuantitySearchValue value = _builder.ToQuantitySearchValue();

            Assert.Equal(system, value.System);
            Assert.Equal(code, value.Code);
        }

        [Fact]
        public void GivenANullString_WhenParsing_ThenExceptionShouldBeThrown()
        {
            Assert.Throws<ArgumentNullException>(ParamNameS, () => TokenSearchValue.Parse(null));
        }

        [Theory]
        [InlineData("")]
        [InlineData("    ")]
        public void GivenAnInvalidString_WhenParsing_ThenExceptionShouldBeThrown(string s)
        {
            Assert.Throws<ArgumentException>(ParamNameS, () => TokenSearchValue.Parse(s));
        }

        [Theory]
        [InlineData(@"abc")]
        [InlineData(@"|system|code")]
        [InlineData(@"abc|system|code")]
        public void GivenAnInvalidNumber_WhenParsing_ThenExceptionShouldBeThrown(string s)
        {
            Assert.Throws<BadRequestException>(() => QuantitySearchValue.Parse(s));
        }

        [Fact]
        public void GivenAStringContainingMoreThanTwoTokenSeparators_WhenParsing_ThenExceptionShouldBeThrown()
        {
            Assert.Throws<FormatException>(() => QuantitySearchValue.Parse(@"50|sys\|tem|co|de|"));
        }

        [Theory]
        [InlineData(@"123.3", "", "", 123.3)]
        [InlineData(@"234.5|system|", "system", "", 234.5)]
        [InlineData(@"0|system|code", "system", "code", 0)]
        [InlineData(@"98.565656||code", "", "code", 98.565656)]
        [InlineData(@"5.40e-3|system|code", "system", "code", 0.00540)]
        [InlineData(@"12|sys\|tem|co\$de", "sys|tem", "co$de", 12)]
        public void GivenAValidString_WhenParsed_ThenCorrectSearchValueShouldBeReturned(string s, string expectedSystem, string expectedCode, decimal expectedQuantity)
        {
            QuantitySearchValue value = QuantitySearchValue.Parse(s);

            Assert.NotNull(value);
            Assert.Equal(expectedSystem, value.System);
            Assert.Equal(expectedCode, value.Code);
            Assert.Equal(expectedQuantity, value.Low);
            Assert.Equal(value.Low, value.High);
        }

        [Fact]
        public void GivenAStringWithTrailingZero_WhenParsed_ThenTrailingZeroShouldBePreserved()
        {
            string expected = "0.010|system|code";

            QuantitySearchValue value = QuantitySearchValue.Parse(expected);

            Assert.NotNull(value);
            Assert.Equal("system", value.System);
            Assert.Equal("code", value.Code);
            Assert.Equal(0.010m, value.Low);
            Assert.Equal(value.Low, value.High);
            Assert.Equal(expected, value.ToString());
        }

        [Fact]
        public void GivenASearchValue_WhenIsValidCompositeComponentIsCalled_ThenTrueShouldBeReturned()
        {
            var value = new QuantitySearchValue("system", "code", 1);

            Assert.True(value.IsValidAsCompositeComponent);
        }

        [Theory]
        [InlineData(12, "system", "code", "12|system|code")]
        [InlineData(3.3, @"sy|ste\|m", @"c$o,\de", @"3.3|sy\|ste\\\|m|c\$o\,\\de")]
        [InlineData(2, null, null, "2")]
        [InlineData(100, null, "code", "100||code")]
        [InlineData(14.2034, "system", null, "14.2034|system")]
        public void GivenASearchValue_WhenToStringIsCalled_ThenCorrectStringShouldBeReturned(decimal quantity, string system, string code, string expected)
        {
            _builder.Value = quantity;
            _builder.System = system;
            _builder.Code = code;

            QuantitySearchValue value = _builder.ToQuantitySearchValue();

            Assert.Equal(expected, value.ToString());
        }

        private class QuantitySearchValueBuilder
        {
            public QuantitySearchValueBuilder()
            {
                System = DefaultSystem;
                Code = DefaultCode;
                Value = DefaultValue;
            }

            public string System { get; set; }

            public string Code { get; set; }

            public decimal Value { get; set; }

            public QuantitySearchValue ToQuantitySearchValue()
            {
                return new QuantitySearchValue(System, Code, Value);
            }
        }
    }
}
