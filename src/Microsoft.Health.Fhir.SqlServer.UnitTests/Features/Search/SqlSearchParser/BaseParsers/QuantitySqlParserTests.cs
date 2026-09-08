// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser;
using Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser.BaseParsers;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.SqlSearchParser.BaseParsers
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class QuantitySqlParserTests
    {
        private readonly QuantitySqlParser _parser;

        public QuantitySqlParserTests()
        {
            _parser = new QuantitySqlParser(ParserTestHelper.CreateMockDefinitionManager());
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
        public void GivenValueOnly_WhenBuildWhereClause_ThenGeneratesNumericConditionOnly()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("5.4", string.Empty, options);

            // Assert
            Assert.Equal("t.HighValue >= @p0 AND t.LowValue <= @p1", result);
            Assert.DoesNotContain("5.4", result, StringComparison.Ordinal);
            Assert.Equal(5.4m, command.Parameters["@p0"].Value);
            Assert.Equal(5.4m, command.Parameters["@p1"].Value);
            Assert.DoesNotContain("SystemId", result);
            Assert.DoesNotContain("QuantityCodeId", result);
        }

        [Fact]
        public void GivenValueWithSystemAndCode_WhenBuildWhereClause_ThenGeneratesAllThreeConditions()
        {
            // Arrange
            const string value = "5.4|http://unitsofmeasure.org|mg";
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause(value, string.Empty, options);

            // Assert
            Assert.Equal("t.HighValue >= @p0 AND t.LowValue <= @p1 AND t.SystemId = (SELECT SystemId FROM dbo.System WHERE Value = @p2) AND t.QuantityCodeId = (SELECT QuantityCodeId FROM dbo.QuantityCode WHERE Value = @p3)", result);
            Assert.DoesNotContain(value, result, StringComparison.Ordinal);
            Assert.Equal(4, command.Parameters.Count);
            Assert.Equal(SqlDbType.Decimal, command.Parameters["@p0"].SqlDbType);
            Assert.Equal(SqlDbType.Decimal, command.Parameters["@p1"].SqlDbType);
            Assert.Equal(5.4m, command.Parameters["@p0"].Value);
            Assert.Equal(5.4m, command.Parameters["@p1"].Value);
            Assert.Equal("http://unitsofmeasure.org", command.Parameters["@p2"].Value);
            Assert.Equal("mg", command.Parameters["@p3"].Value);
        }

        [Fact]
        public void GivenValueWithCodeOnly_WhenBuildWhereClause_ThenGeneratesValueAndCodeConditions()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("5.4||mg", string.Empty, options);

            // Assert
            Assert.Equal("t.HighValue >= @p0 AND t.LowValue <= @p1 AND t.QuantityCodeId = (SELECT QuantityCodeId FROM dbo.QuantityCode WHERE Value = @p2)", result);
            Assert.Equal(5.4m, command.Parameters["@p0"].Value);
            Assert.Equal(5.4m, command.Parameters["@p1"].Value);
            Assert.Equal("mg", command.Parameters["@p2"].Value);
            Assert.DoesNotContain("SystemId", result);
        }

        [Fact]
        public void GivenGtPrefix_WhenBuildWhereClause_ThenUsesHighValueGreaterThan()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("gt50|http://unitsofmeasure.org|kg", string.Empty, options);

            // Assert
            Assert.Equal("t.HighValue > @p0 AND t.SystemId = (SELECT SystemId FROM dbo.System WHERE Value = @p1) AND t.QuantityCodeId = (SELECT QuantityCodeId FROM dbo.QuantityCode WHERE Value = @p2)", result);
            Assert.Equal(50m, command.Parameters["@p0"].Value);
            Assert.Equal("http://unitsofmeasure.org", command.Parameters["@p1"].Value);
            Assert.Equal("kg", command.Parameters["@p2"].Value);
        }

        [Fact]
        public void GivenLePrefix_WhenBuildWhereClause_ThenUsesLowValueLessOrEqual()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("le100.0", string.Empty, options);

            // Assert
            Assert.Equal("t.LowValue <= @p0", result);
            Assert.Equal(100.0m, command.Parameters["@p0"].Value);
        }

        [Fact]
        public void GivenColumnSuffix_WhenBuildWhereClause_ThenAppendsSuffixToColumnNames()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("5.4", string.Empty, options, columnSuffix: 2);

            // Assert
            Assert.Equal("t.HighValue2 >= @p0 AND t.LowValue2 <= @p1", result);
        }

        [Fact]
        public void GivenCustomTableName_WhenBuildWhereClause_ThenUsesCustomTableName()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("5.4", string.Empty, options, tableName: "q");

            // Assert
            Assert.Equal("q.HighValue >= @p0 AND q.LowValue <= @p1", result);
        }

        [Fact]
        public void GivenApPrefix_WhenBuildWhereClause_ThenGeneratesApproximateConditionWithBoundNumericValues()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("ap100", string.Empty, options);

            // Assert
            Assert.Equal("(t.HighValue >= @p0 * 0.9 AND t.LowValue <= @p1 * 1.1)", result);
            Assert.Contains("0.9", result);
            Assert.Contains("1.1", result);
            Assert.DoesNotContain("100", result, StringComparison.Ordinal);
            Assert.Equal(100m, command.Parameters["@p0"].Value);
            Assert.Equal(100m, command.Parameters["@p1"].Value);
        }
    }
}
