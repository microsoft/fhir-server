// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.SqlSearchParser
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class QueryStringParserTests
    {
        [Fact]
        public void GivenNullInput_WhenParse_ThenReturnsEmptyDictionary()
        {
            var result = QueryStringParser.Parse(null);
            Assert.Empty(result);
        }

        [Fact]
        public void GivenEmptyString_WhenParse_ThenReturnsEmptyDictionary()
        {
            var result = QueryStringParser.Parse(string.Empty);
            Assert.Empty(result);
        }

        [Fact]
        public void GivenNoQuestionMark_WhenParse_ThenReturnsEmptyDictionary()
        {
            var result = QueryStringParser.Parse("name=value");
            Assert.Empty(result);
        }

        [Fact]
        public void GivenSingleParam_WhenParse_ThenReturnsSingleEntry()
        {
            var result = QueryStringParser.Parse("http://host?name=value");
            Assert.Single(result);
            Assert.Equal("value", result["name"][0]);
        }

        [Fact]
        public void GivenMultipleParams_WhenParse_ThenReturnsAllEntries()
        {
            var result = QueryStringParser.Parse("http://host?a=1&b=2&c=3");
            Assert.Equal(3, result.Count);
            Assert.Equal("1", result["a"][0]);
            Assert.Equal("2", result["b"][0]);
            Assert.Equal("3", result["c"][0]);
        }

        [Fact]
        public void GivenDuplicateKeys_WhenParse_ThenReturnsListWithMultipleValues()
        {
            var result = QueryStringParser.Parse("http://host?a=1&a=2&a=3");
            Assert.Single(result);
            Assert.Equal(3, result["a"].Count);
            Assert.Equal("1", result["a"][0]);
            Assert.Equal("2", result["a"][1]);
            Assert.Equal("3", result["a"][2]);
        }

        [Fact]
        public void GivenUrlEncodedValue_WhenParse_ThenDecodesValue()
        {
            var result = QueryStringParser.Parse("http://host?name=hello%20world");
            Assert.Equal("hello world", result["name"][0]);
        }

        [Fact]
        public void GivenParamWithoutValue_WhenParse_ThenReturnsEmptyStringValue()
        {
            var result = QueryStringParser.Parse("http://host?flag");
            Assert.Equal(string.Empty, result["flag"][0]);
        }

        [Fact]
        public void GivenParamWithEmptyValue_WhenParse_ThenReturnsEmptyStringValue()
        {
            var result = QueryStringParser.Parse("http://host?name=");
            Assert.Equal(string.Empty, result["name"][0]);
        }

        [Fact]
        public void GivenCaseInsensitiveKeys_WhenParse_ThenGroupsTogether()
        {
            var result = QueryStringParser.Parse("http://host?Name=a&name=b");
            Assert.Single(result);
            Assert.Equal(2, result["name"].Count);
        }
    }
}
