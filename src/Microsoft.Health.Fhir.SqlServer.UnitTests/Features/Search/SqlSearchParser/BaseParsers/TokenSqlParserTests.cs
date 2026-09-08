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
            var result = _parser.BuildWhereClause(string.Empty, string.Empty, new ParserOptions());

            // Assert
            Assert.Equal("1=1", result);
        }

        [Fact]
        public void GivenCodeOnly_WhenBuildWhereClause_ThenGeneratesCodeCondition()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("active", string.Empty, options);

            // Assert
            Assert.Equal("t.Code = @p0", result);
            Assert.Equal("active", command.Parameters["@p0"].Value);
        }

        [Fact]
        public void GivenSystemAndCode_WhenBuildWhereClause_ThenGeneratesSystemAndCodeConditions()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("http://sys|active", string.Empty, options);

            // Assert
            Assert.Contains("t.SystemId = (SELECT SystemId FROM dbo.System WHERE Value = @p0)", result);
            Assert.Contains("t.Code = @p1", result);
            Assert.Contains(" AND ", result);
            Assert.Equal("http://sys", command.Parameters["@p0"].Value);
            Assert.Equal("active", command.Parameters["@p1"].Value);
        }

        [Fact]
        public void GivenEmptySystem_WhenBuildWhereClause_ThenGeneratesNullOrEmptySystemCondition()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("|active", string.Empty, options);

            // Assert
            Assert.Contains("SystemId", result);
            Assert.Contains("IS NULL", result);
            Assert.Contains("t.Code = @p1", result);
            Assert.Equal(string.Empty, command.Parameters["@p0"].Value);
            Assert.Equal("active", command.Parameters["@p1"].Value);
        }

        [Fact]
        public void GivenSystemOnly_WhenBuildWhereClause_ThenGeneratesSystemConditionWithoutCode()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("http://sys|", string.Empty, options);

            // Assert
            Assert.Contains("t.SystemId = (SELECT SystemId FROM dbo.System WHERE Value = @p0)", result);
            Assert.DoesNotContain("Code", result);
            Assert.Equal("http://sys", command.Parameters["@p0"].Value);
        }

        [Fact]
        public void GivenTextModifier_WhenBuildWhereClause_ThenGeneratesTextLikeCondition()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("active", "text", options);

            // Assert
            Assert.Equal("(t.Text LIKE @p0)", result);
            Assert.Equal("active%", command.Parameters["@p0"].Value);
        }

        [Fact]
        public void GivenLongCode_WhenBuildWhereClause_ThenUsesCodeAndCodeOverflow()
        {
            // Arrange
            var longCode = new string('x', 300);
            var expectedPrefix = longCode.Substring(0, 256);
            var expectedOverflow = longCode.Substring(256);
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause(longCode, string.Empty, options);

            // Assert
            Assert.Contains("t.Code = @p0", result);
            Assert.Contains("t.CodeOverflow = @p1", result);
            Assert.Equal(expectedPrefix, command.Parameters["@p0"].Value);
            Assert.Equal(expectedOverflow, command.Parameters["@p1"].Value);
        }

        [Fact]
        public void GivenValueWithSingleQuote_WhenBuildWhereClause_ThenEscapesQuote()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("o'brian", string.Empty, options);

            // Assert
            Assert.Equal("t.Code = @p0", result);
            Assert.Equal("o'brian", command.Parameters["@p0"].Value);
        }

        [Fact]
        public void GivenTextModifierWithSingleQuote_WhenBuildWhereClause_ThenEscapesQuote()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("o'test", "text", options);

            // Assert
            Assert.Equal("(t.Text LIKE @p0)", result);
            Assert.Equal("o'test%", command.Parameters["@p0"].Value);
        }

        [Fact]
        public void GivenColumnSuffix_WhenBuildWhereClause_ThenAppendsSuffixToColumnNames()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("active", string.Empty, options, columnSuffix: 2);

            // Assert
            Assert.Contains("t.Code2 = @p0", result);
            Assert.Equal("active", command.Parameters["@p0"].Value);
        }

        [Fact]
        public void GivenCustomTableName_WhenBuildWhereClause_ThenUsesCustomTableName()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("active", string.Empty, options, tableName: "sp");

            // Assert
            Assert.Contains("sp.Code = @p0", result);
            Assert.Equal("active", command.Parameters["@p0"].Value);
        }
    }
}
