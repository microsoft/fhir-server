// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.SqlSearchParser.BaseParsers
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class UriSqlParserTests
    {
        private readonly UriSqlParser _parser;

        public UriSqlParserTests()
        {
            _parser = new UriSqlParser(ParserTestHelper.CreateMockDefinitionManager());
        }

        [Fact]
        public void GivenEmptyValue_WhenBuildWhereClause_ThenReturnsAlwaysTrue()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause(string.Empty, string.Empty);

            // Assert
            Assert.Equal("1=1", result);
        }

        [Fact]
        public void GivenSimpleUri_WhenBuildWhereClause_ThenGeneratesExactMatchCondition()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("http://example.org/profile", string.Empty);

            // Assert
            Assert.Equal("t.Uri = 'http://example.org/profile'", result);
        }

        [Fact]
        public void GivenAboveModifier_WhenBuildWhereClause_ThenGeneratesAncestorCondition()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("http://example.org/a/b", "above");

            // Assert
            Assert.Contains("LIKE t.Uri", result);
            Assert.Contains("NOT LIKE 'urn:%'", result);
        }

        [Fact]
        public void GivenBelowModifier_WhenBuildWhereClause_ThenGeneratesDescendantCondition()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("http://example.org/a", "below");

            // Assert
            Assert.Contains("t.Uri", result);
            Assert.Contains("LIKE 'http://example.org/a'", result);
            Assert.Contains("NOT LIKE 'urn:%'", result);
        }

        [Fact]
        public void GivenUnknownModifier_WhenBuildWhereClause_ThenFallsBackToExactMatch()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("http://example.org/profile", "unknown");

            // Assert
            Assert.Equal("t.Uri = 'http://example.org/profile'", result);
        }

        [Fact]
        public void GivenUriWithSingleQuote_WhenBuildWhereClause_ThenEscapesQuote()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("http://example.org/a'b", string.Empty);

            // Assert
            Assert.Contains("a''b", result);
        }

        [Fact]
        public void GivenColumnSuffix_WhenBuildWhereClause_ThenAppendsSuffixToColumnName()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("http://example.org/profile", string.Empty, columnSuffix: 2);

            // Assert
            Assert.Contains("t.Uri2 = 'http://example.org/profile'", result);
        }

        [Fact]
        public void GivenCustomTableName_WhenBuildWhereClause_ThenUsesCustomTableName()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("http://example.org/profile", string.Empty, tableName: "u");

            // Assert
            Assert.Contains("u.Uri = 'http://example.org/profile'", result);
        }
    }
}
