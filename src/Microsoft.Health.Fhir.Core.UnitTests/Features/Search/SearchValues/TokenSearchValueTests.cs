// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.SearchValues
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class TokenSearchValueTests
    {
        private const string ParamNameS = "s";

        private const string DefaultSystem = "system";
        private const string DefaultCode = "code";
        private const string DefaultText = "text";

        private TokenSearchValueBuilder _builder = new TokenSearchValueBuilder();

        public static IEnumerable<object[]> GetEmptySystemCodeTextCombo()
        {
            string[] values = new string[] { null, string.Empty, "    " };

            return from s1 in values
                   from s2 in values
                   from s3 in values
                   select new object[] { s1, s2, s3 };
        }

        public static IEnumerable<object[]> GetNonEmptySystemCodeTextCombo()
        {
            string[] values = new string[] { null, string.Empty, "    ", "text" };

            return from s1 in values
                   from s2 in values
                   from s3 in values
                   where !(string.IsNullOrWhiteSpace(s1) && string.IsNullOrWhiteSpace(s2) && string.IsNullOrWhiteSpace(s3))
                   select new object[] { s1, s2, s3 };
        }

        [Theory]
        [MemberData(nameof(GetEmptySystemCodeTextCombo))]
        public void GiveEmptySystemCodeAndText_WhenInitializing_ThenExceptionShouldBeThrown(string system, string code, string text)
        {
            _builder.System = system;
            _builder.Code = code;
            _builder.Text = text;

            Assert.Throws<ArgumentException>(() => _builder.ToTokenSearchValue());
        }

        [Theory]
        [MemberData(nameof(GetNonEmptySystemCodeTextCombo))]
        public void GivenOneNonEmptyField_WhenInitialized_ThenTokenSearchValueShouldBeCreated(string system, string code, string text)
        {
            _builder.System = system;
            _builder.Code = code;
            _builder.Text = text;

            TokenSearchValue value = _builder.ToTokenSearchValue();

            Assert.Equal(text, value.Text);
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

        [Fact]
        public void GivenAStringContainingMoreThanOneTokenSeparator_WhenParsing_ThenExceptionShouldBeThrown()
        {
            Assert.Throws<FormatException>(() => TokenSearchValue.Parse(@"s12\|s12|c12\|c12|c12"));
        }

        [Theory]
        [InlineData(@"\|", null, @"|")]
        [InlineData(@"c1", null, "c1")]
        [InlineData(@"|c2", "", "c2")]
        [InlineData(@"s3|", "s3", "")]
        [InlineData(@"s4|c4", "s4", "c4")]
        [InlineData(@"c5\|c5", null, @"c5|c5")]
        [InlineData(@"s6\|s6|", @"s6|s6", "")]
        [InlineData(@"s7\|s7|c7", @"s7|s7", "c7")]
        [InlineData(@"s8\|s8\|s8|c8", @"s8|s8|s8", "c8")]
        [InlineData(@"s9\|s9\|s9|c9\|", @"s9|s9|s9", @"c9|")]
        [InlineData(@"s10\|\|s10|c10\|c10", @"s10||s10", @"c10|c10")]
        [InlineData(@"s11\|\|s11|c11\|c11\|c11", @"s11||s11", @"c11|c11|c11")]
        [InlineData(@"s12\\|s12", @"s12\", "s12")]
        public void GivenAValidString_WhenParsed_ThenCorrectSearchValueShouldBeReturned(string s, string expectedSystem, string expectedCode)
        {
            TokenSearchValue value = TokenSearchValue.Parse(s);

            Assert.NotNull(value);
            Assert.Equal(expectedSystem, value.System);
            Assert.Equal(expectedCode, value.Code);
            Assert.Null(value.Text);
        }

        [Theory]
        [InlineData(null, null, false)]
        [InlineData(null, "code", true)]
        [InlineData("system", null, true)]
        [InlineData("system", "code", true)]
        public void GivenASearchValue_WhenIsValidCompositeComponentIsCalled_ThenCorrectValueShouldBeReturned(string system, string code, bool expected)
        {
            var value = new TokenSearchValue(system, code, "test");

            Assert.Equal(expected, value.IsValidAsCompositeComponent);
        }

        [Theory]
        [InlineData(@"system", "code", "system|code")]
        [InlineData(@"sys\|tem", @"\$code", @"sys\\\|tem|\\\$code")]
        [InlineData(null, "code", "code")]
        [InlineData("", "code", "|code")]
        public void GivenASearchValue_WhenToStringIsCalled_ThenCorrectStringShouldBeReturned(string system, string code, string expected)
        {
            _builder.System = system;
            _builder.Code = code;

            TokenSearchValue value = _builder.ToTokenSearchValue();

            Assert.Equal(expected, value.ToString());
        }

        private class TokenSearchValueBuilder
        {
            public TokenSearchValueBuilder()
            {
                System = DefaultSystem;
                Code = DefaultCode;
                Text = DefaultText;
            }

            public string System { get; set; }

            public string Code { get; set; }

            public string Text { get; set; }

            public TokenSearchValue ToTokenSearchValue()
            {
                return new TokenSearchValue(System, Code, Text);
            }
        }
    }
}
