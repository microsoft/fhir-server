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
    public class StringSqlParserTests
    {
        private readonly StringSqlParser _parser;

        public StringSqlParserTests()
        {
            _parser = new StringSqlParser(ParserTestHelper.CreateMockDefinitionManager());
        }

        [Fact]
        public void GivenDefaultModifier_WhenBuildWhereClause_ThenGeneratesStartsWithCondition()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("Smith", string.Empty, options);

            // Assert
            Assert.Equal("(t.Text like @p0)", result);
            Assert.Equal("Smith%", command.Parameters["@p0"].Value);
        }

        [Fact]
        public void GivenExactModifier_WhenBuildWhereClause_ThenGeneratesExactMatchWithCollation()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("Smith", "exact", options);

            // Assert
            Assert.Equal("t.Text = @p0 COLLATE Latin1_General_100_CS_AS", result);
            Assert.Equal("Smith", command.Parameters["@p0"].Value);
        }

        [Fact]
        public void GivenContainsModifier_WhenBuildWhereClause_ThenGeneratesContainsCondition()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("mit", "contains", options);

            // Assert
            Assert.Equal("(t.Text like @p0)", result);
            Assert.Equal("%mit%", command.Parameters["@p0"].Value);
        }

        [Fact]
        public void GivenEscapedLongValueWithDefaultModifier_WhenBuildWhereClause_ThenUsesTextOverflowColumn()
        {
            // Arrange
            var rawValue = $"{new string('a', 255)}'";
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause(rawValue, string.Empty, options);

            // Assert
            Assert.Equal("(t.TextOverflow like @p0)", result);
            Assert.DoesNotContain(rawValue, result, StringComparison.Ordinal);
            Assert.Equal($"{rawValue}%", command.Parameters["@p0"].Value);
        }

        [Fact]
        public void GivenLongValueWithDefaultModifier_WhenBuildWhereClause_ThenUsesTextOverflowColumn()
        {
            // Arrange
            var longValue = new string('a', 257);
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause(longValue, string.Empty, options);

            // Assert
            Assert.Contains("t.TextOverflow", result);
            Assert.Equal($"{longValue}%", command.Parameters["@p0"].Value);
        }

        [Fact]
        public void GivenLongValueWithExactModifier_WhenBuildWhereClause_ThenUsesTextOverflowColumn()
        {
            // Arrange
            var longValue = new string('a', 257);
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause(longValue, "exact", options);

            // Assert
            Assert.Contains("t.TextOverflow", result);
            Assert.Contains("COLLATE Latin1_General_100_CS_AS", result);
            Assert.Equal(longValue, command.Parameters["@p0"].Value);
        }

        [Fact]
        public void GivenColumnSuffix_WhenBuildWhereClause_ThenAppendsSuffixToColumnName()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("Smith", string.Empty, options, columnSuffix: 3);

            // Assert
            Assert.Contains("t.Text3", result);
            Assert.Equal("Smith%", command.Parameters["@p0"].Value);
        }

        [Fact]
        public void GivenValueWithSingleQuote_WhenBuildWhereClause_ThenEscapesQuote()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("O'Brien", string.Empty, options);

            // Assert
            Assert.DoesNotContain("O'Brien", result, StringComparison.Ordinal);
            Assert.Equal("O'Brien%", command.Parameters["@p0"].Value);
        }

        [Fact]
        public void GivenCustomTableName_WhenBuildWhereClause_ThenUsesCustomTableName()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("Smith", string.Empty, options, tableName: "sp");

            // Assert
            Assert.Contains("sp.Text", result);
            Assert.Equal("Smith%", command.Parameters["@p0"].Value);
        }
    }
}
