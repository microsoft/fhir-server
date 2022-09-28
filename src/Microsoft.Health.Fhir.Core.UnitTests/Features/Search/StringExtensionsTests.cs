// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [Trait(Traits.Category, Categories.Operations)]
    public class StringExtensionsTests
    {
        private const string ParamNameS = "s";

        [Fact]
        public void GivenANullString_WhenSplittingByTokenSeparator_ThenExceptionShouldBeThrown()
        {
            string s = null;

            Assert.Throws<ArgumentNullException>(ParamNameS, () => s.SplitByTokenSeparator());
        }

        [Theory]
        [InlineData(@"", "")]
        [InlineData(@"    ", "    ")]
        [InlineData(@"|", "", "")]
        [InlineData(@"\|", @"\|")]
        [InlineData(@"a1", "a1")]
        [InlineData(@"|b2", "", "b2")]
        [InlineData(@"a3|", "a3", "")]
        [InlineData(@"a4|b4", "a4", "b4")]
        [InlineData(@"a5\|a5", @"a5\|a5")]
        [InlineData(@"a6\|a6|", @"a6\|a6", "")]
        [InlineData(@"a7\|a7|b7", @"a7\|a7", "b7")]
        [InlineData(@"a8\|a8\|a8|b8", @"a8\|a8\|a8", "b8")]
        [InlineData(@"a9\|a9\|a9|b9\|", @"a9\|a9\|a9", @"b9\|")]
        [InlineData(@"a10\|\|a10|b10\|b10\|b10", @"a10\|\|a10", @"b10\|b10\|b10")]
        [InlineData(@"a11\|a11|b11\|b11|c11", @"a11\|a11", @"b11\|b11", @"c11")]
        [InlineData(@"a12\|a12|b12\|b12|c12|", @"a12\|a12", @"b12\|b12", @"c12", "")]
        [InlineData(@"a13\\\\|a13", @"a13\\\\", @"a13")]
        [InlineData(@"a14\\\|a14", @"a14\\\|a14")]
        public void GivenAString_WhenSplitByTokenSeparator_ThenCorrectTokensShouldBeReturned(string s, params string[] expected)
        {
            Assert.Equal(expected, s.SplitByTokenSeparator());
        }

        [Fact]
        public void GivenANullString_WhenSplittingByCompositeSeparator_ThenExceptionShouldBeThrown()
        {
            string s = null;

            Assert.Throws<ArgumentNullException>(ParamNameS, () => s.SplitByCompositeSeparator());
        }

        [Theory]
        [InlineData(@"$", "", "")]
        [InlineData(@"$b1", "", "b1")]
        [InlineData(@"a2$", "a2", "")]
        [InlineData(@"a3$b3", "a3", "b3")]
        [InlineData(@"a4\$a4$", @"a4\$a4", "")]
        [InlineData(@"a5\$a5$b5", @"a5\$a5", "b5")]
        [InlineData(@"a6\$a6\$a6$b6", @"a6\$a6\$a6", "b6")]
        [InlineData(@"a7\$a7\$a7$b7\$", @"a7\$a7\$a7", @"b7\$")]
        [InlineData(@"a8\$\$a8$b8\$b8\$b8", @"a8\$\$a8", @"b8\$b8\$b8")]
        [InlineData(@"a9\\\\$a9", @"a9\\\\", @"a9")]
        public void GivenAString_WhenSplitByCompositeSeparator_ThenCorrectTokensShouldBeReturned(string s, params string[] expected)
        {
            Assert.Equal(expected, s.SplitByCompositeSeparator());
        }

        [Theory]
        [InlineData(null, null)]
        [InlineData(@"", "")]
        [InlineData(@"    ", "    ")]
        [InlineData(@"abc\def", @"abc\\def")]
        [InlineData(@"abc$def", @"abc\$def")]
        [InlineData(@"123|456", @"123\|456")]
        [InlineData(@"XYZ,789", @"XYZ\,789")]
        [InlineData(@"abc,def|xyz$123\!@#", @"abc\,def\|xyz\$123\\!@#")]
        [InlineData(@"abc\,def\|xyz\$123\\!@#", @"abc\\\,def\\\|xyz\\\$123\\\\!@#")]
        public void GivenAString_WhenEscapedSearchParameterValue_ThenCorrectStringShouldBeReturned(string s, string expected)
        {
            Assert.Equal(expected, s.EscapeSearchParameterValue());
        }

        [Theory]
        [InlineData(null, null)]
        [InlineData(@"", "")]
        [InlineData(@"    ", "    ")]
        [InlineData(@"abc\\def", @"abc\def")]
        [InlineData(@"abc\$def", @"abc$def")]
        [InlineData(@"123\|456", @"123|456")]
        [InlineData(@"XYZ\,789", @"XYZ,789")]
        [InlineData(@"abc\,def\|xyz\$123\\!@#", @"abc,def|xyz$123\!@#")]
        [InlineData(@"abc\\\,def\\\|xyz\\\$123\\\\!@#", @"abc\,def\|xyz\$123\\!@#")]
        public void GivenAString_WhenUnescapedSearchParameterValue_ThenCorrectStringShouldBeReturned(string s, string expected)
        {
            Assert.Equal(expected, s.UnescapeSearchParameterValue());
        }
    }
}
