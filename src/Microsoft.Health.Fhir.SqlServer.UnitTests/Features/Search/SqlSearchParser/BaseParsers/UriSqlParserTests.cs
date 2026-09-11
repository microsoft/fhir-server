// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Data.SqlClient;
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
            var result = _parser.BuildWhereClause(string.Empty, string.Empty, new ParserOptions());

            // Assert
            Assert.Equal("1=1", result);
        }

        [Fact]
        public void GivenSimpleUri_WhenBuildWhereClause_ThenGeneratesExactMatchCondition()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("http://example.org/profile", string.Empty, options);

            // Assert
            Assert.Equal("t.Uri = @p0", result);
            Assert.Equal("http://example.org/profile", command.Parameters["@p0"].Value);
        }

        [Fact]
        public void GivenAboveModifier_WhenBuildWhereClause_ThenGeneratesAncestorCondition()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("http://example.org/a/b", "above", options);

            // Assert
            Assert.Equal("(@p0 LIKE t.Uri + '%' AND t.Uri NOT LIKE 'urn:%')", result);
            Assert.Contains("NOT LIKE 'urn:%'", result);
            Assert.Equal("http://example.org/a/b", command.Parameters["@p0"].Value);
        }

        [Fact]
        public void GivenBelowModifier_WhenBuildWhereClause_ThenGeneratesDescendantCondition()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("http://example.org/a", "below", options);

            // Assert
            Assert.Equal("(t.Uri LIKE @p0 AND t.Uri NOT LIKE 'urn:%')", result);
            Assert.Contains("NOT LIKE 'urn:%'", result);
            Assert.Equal("http://example.org/a%", command.Parameters["@p0"].Value);
        }

        [Fact]
        public void GivenUnknownModifier_WhenBuildWhereClause_ThenFallsBackToExactMatch()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("http://example.org/profile", "unknown", options);

            // Assert
            Assert.Equal("t.Uri = @p0", result);
            Assert.Equal("http://example.org/profile", command.Parameters["@p0"].Value);
        }

        [Fact]
        public void GivenUriWithSingleQuote_WhenBuildWhereClause_ThenEscapesQuote()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("http://example.org/a'b", string.Empty, options);

            // Assert
            Assert.Equal("t.Uri = @p0", result);
            Assert.DoesNotContain("a'b", result, StringComparison.Ordinal);
            Assert.Equal("http://example.org/a'b", command.Parameters["@p0"].Value);
        }

        [Fact]
        public void GivenColumnSuffix_WhenBuildWhereClause_ThenAppendsSuffixToColumnName()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("http://example.org/profile", string.Empty, options, columnSuffix: 2);

            // Assert
            Assert.Contains("t.Uri2 = @p0", result);
            Assert.Equal("http://example.org/profile", command.Parameters["@p0"].Value);
        }

        [Fact]
        public void GivenCustomTableName_WhenBuildWhereClause_ThenUsesCustomTableName()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("http://example.org/profile", string.Empty, options, tableName: "u");

            // Assert
            Assert.Contains("u.Uri = @p0", result);
            Assert.Equal("http://example.org/profile", command.Parameters["@p0"].Value);
        }
    }
}
