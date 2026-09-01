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
            // Arrange / Act
            var result = _parser.BuildWhereClause("Smith", string.Empty);

            // Assert
            Assert.Equal("(t.Text like N'Smith%')", result);
        }

        [Fact]
        public void GivenExactModifier_WhenBuildWhereClause_ThenGeneratesExactMatchWithCollation()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("Smith", "exact");

            // Assert
            Assert.Equal("t.Text = N'Smith' COLLATE Latin1_General_100_CS_AS", result);
        }

        [Fact]
        public void GivenContainsModifier_WhenBuildWhereClause_ThenGeneratesContainsCondition()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("mit", "contains");

            // Assert
            Assert.Equal("(t.Text like N'%mit%')", result);
        }

        [Fact]
        public void GivenLongValueWithDefaultModifier_WhenBuildWhereClause_ThenUsesTextOverflowColumn()
        {
            // Arrange
            var longValue = new string('a', 257);

            // Act
            var result = _parser.BuildWhereClause(longValue, string.Empty);

            // Assert
            Assert.Contains("t.TextOverflow", result);
        }

        [Fact]
        public void GivenLongValueWithExactModifier_WhenBuildWhereClause_ThenUsesTextOverflowColumn()
        {
            // Arrange
            var longValue = new string('a', 257);

            // Act
            var result = _parser.BuildWhereClause(longValue, "exact");

            // Assert
            Assert.Contains("t.TextOverflow", result);
            Assert.Contains("COLLATE Latin1_General_100_CS_AS", result);
        }

        [Fact]
        public void GivenColumnSuffix_WhenBuildWhereClause_ThenAppendsSuffixToColumnName()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("Smith", string.Empty, columnSuffix: 3);

            // Assert
            Assert.Contains("t.Text3", result);
        }

        [Fact]
        public void GivenValueWithSingleQuote_WhenBuildWhereClause_ThenEscapesQuote()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("O'Brien", string.Empty);

            // Assert
            Assert.Contains("O''Brien", result);
        }

        [Fact]
        public void GivenCustomTableName_WhenBuildWhereClause_ThenUsesCustomTableName()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("Smith", string.Empty, tableName: "sp");

            // Assert
            Assert.Contains("sp.Text", result);
        }
    }
}
