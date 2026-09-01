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
    public class TokenSqlParserTests
    {
        private readonly TokenSqlParser _parser;

        public TokenSqlParserTests()
        {
            _parser = new TokenSqlParser(ParserTestHelper.CreateMockDefinitionManager());
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
        public void GivenCodeOnly_WhenBuildWhereClause_ThenGeneratesCodeCondition()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("active", string.Empty);

            // Assert
            Assert.Equal("t.Code = 'active'", result);
        }

        [Fact]
        public void GivenSystemAndCode_WhenBuildWhereClause_ThenGeneratesSystemAndCodeConditions()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("http://sys|active", string.Empty);

            // Assert
            Assert.Contains("t.SystemId = (SELECT SystemId FROM dbo.System WHERE Value = 'http://sys')", result);
            Assert.Contains("t.Code = 'active'", result);
            Assert.Contains(" AND ", result);
        }

        [Fact]
        public void GivenEmptySystem_WhenBuildWhereClause_ThenGeneratesNullOrEmptySystemCondition()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("|active", string.Empty);

            // Assert
            Assert.Contains("SystemId", result);
            Assert.Contains("IS NULL", result);
            Assert.Contains("t.Code = 'active'", result);
        }

        [Fact]
        public void GivenSystemOnly_WhenBuildWhereClause_ThenGeneratesSystemConditionWithoutCode()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("http://sys|", string.Empty);

            // Assert
            Assert.Contains("t.SystemId = (SELECT SystemId FROM dbo.System WHERE Value = 'http://sys')", result);
            Assert.DoesNotContain("Code", result);
        }

        [Fact]
        public void GivenTextModifier_WhenBuildWhereClause_ThenGeneratesTextLikeCondition()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("active", "text");

            // Assert
            Assert.Equal("(t.Text LIKE N'active%')", result);
        }

        [Fact]
        public void GivenLongCode_WhenBuildWhereClause_ThenUsesCodeAndCodeOverflow()
        {
            // Arrange
            var longCode = new string('x', 300);
            var expectedPrefix = longCode.Substring(0, 256);
            var expectedOverflow = longCode.Substring(256);

            // Act
            var result = _parser.BuildWhereClause(longCode, string.Empty);

            // Assert
            Assert.Contains($"t.Code = '{expectedPrefix}'", result);
            Assert.Contains($"t.CodeOverflow = '{expectedOverflow}'", result);
        }

        [Fact]
        public void GivenValueWithSingleQuote_WhenBuildWhereClause_ThenEscapesQuote()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("o'brian", string.Empty);

            // Assert
            Assert.Contains("o''brian", result);
        }

        [Fact]
        public void GivenTextModifierWithSingleQuote_WhenBuildWhereClause_ThenEscapesQuote()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("o'test", "text");

            // Assert
            Assert.Contains("o''test", result);
        }

        [Fact]
        public void GivenColumnSuffix_WhenBuildWhereClause_ThenAppendsSuffixToColumnNames()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("active", string.Empty, columnSuffix: 2);

            // Assert
            Assert.Contains("t.Code2 = 'active'", result);
        }

        [Fact]
        public void GivenCustomTableName_WhenBuildWhereClause_ThenUsesCustomTableName()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("active", string.Empty, tableName: "sp");

            // Assert
            Assert.Contains("sp.Code = 'active'", result);
        }
    }
}
