// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.Legacy;
using Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.SearchValues
{
    public class CompositeSearchValueTests
    {
        private const string ParamNameValue = "value";
        private const string ParamNameS = "s";

        private const string DefaultSystem = "system";
        private const string DefaultCode = "code";
        private static readonly StringSearchValue DefaultValue = new StringSearchValue("value");
        private static readonly SearchParamValueParser DefaultParser = StringSearchValue.Parse;

        private CompositeSearchValueBuilder _builder = new CompositeSearchValueBuilder();

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(DefaultSystem)]
        public void GivenASystem_WhenInitialized_ThenCorrectSystemShouldBeAssigned(string s)
        {
            _builder.System = s;

            LegacyCompositeSearchValue value = _builder.ToCompositeSearchValue();

            Assert.Equal(s, value.System);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(DefaultCode)]
        public void GivenACode_WhenInitialized_ThenCorrectCodeShouldBeAssigned(string s)
        {
            _builder.Code = s;

            LegacyCompositeSearchValue value = _builder.ToCompositeSearchValue();

            Assert.Equal(s, value.Code);
        }

        [Fact]
        public void GivenANullValue_WhenInitializing_ThenExceptionShouldBeThrown()
        {
            _builder.Value = null;

            Assert.Throws<ArgumentNullException>(ParamNameValue, () => _builder.ToCompositeSearchValue());
        }

        [Fact]
        public void GivenANullString_WhenParsing_ThenExceptionShouldBeThrown()
        {
            Assert.Throws<ArgumentNullException>(ParamNameS, () => LegacyCompositeSearchValue.Parse(null, DefaultParser));
        }

        [Theory]
        [InlineData("")]
        [InlineData("    ")]
        public void GivenAnInvalidString_WhenParsing_ThenExceptionShouldBeThrown(string s)
        {
            Assert.Throws<ArgumentException>(ParamNameS, () => LegacyCompositeSearchValue.Parse(s, DefaultParser));
        }

        [Theory]
        [InlineData(@"sys|co$abc", "sys", "co", "abc")]
        [InlineData(@"|co$abc", "", "co", "abc")]
        [InlineData(@"co$abc", null, "co", "abc")]
        [InlineData(@"sys|$abc", "sys", "", "abc")]
        [InlineData(@"sys\$tem|@co\$de$abc", @"sys$tem", @"@co$de", "abc")]
        [InlineData(@"system\\|code\\\\$abc", @"system\", @"code\\", "abc")]
        public void GivenAValidString_WhenParsed_ThenCorrectSearchValueShouldBeReturned(string input, string expectedSystem, string expectedCode, string expectedValue)
        {
            LegacyCompositeSearchValue value = LegacyCompositeSearchValue.Parse(input, StringSearchValue.Parse);

            Assert.NotNull(value);
            Assert.Equal(expectedSystem, value.System);
            Assert.Equal(expectedCode, value.Code);
            Assert.NotNull(value.Value);
            Assert.IsType<StringSearchValue>(value.Value);
            Assert.Equal(expectedValue, ((StringSearchValue)value.Value).String);
        }

        [Theory]
        [InlineData("system", "code", "123", "system|code$123")]
        [InlineData(@"sy$t|$e|m", "code", "123", @"sy\$t\|\$e\|m|code$123")]
        [InlineData(@"system", "code", @"123$\|def", @"system|code$123\$\\\|def")]
        [InlineData(null, null, "123", @"$123")]
        [InlineData(null, "code", "123", @"code$123")]
        [InlineData("system", "", "123", @"system|$123")]
        public void GivenASearchValue_WhenToStringIsCalled_ThenCorrectStringShouldBeReturned(string system, string code, string s, string expected)
        {
            LegacyCompositeSearchValue value = new LegacyCompositeSearchValue(system, code, new StringSearchValue(s));

            Assert.Equal(expected, value.ToString());
        }

        private class CompositeSearchValueBuilder
        {
            public CompositeSearchValueBuilder()
            {
                System = DefaultSystem;
                Code = DefaultCode;
                Value = DefaultValue;
            }

            public string System { get; set; }

            public string Code { get; set; }

            public StringSearchValue Value { get; set; }

            public LegacyCompositeSearchValue ToCompositeSearchValue()
            {
                return new LegacyCompositeSearchValue(System, Code, Value);
            }
        }
    }
}
