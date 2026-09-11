// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.SqlSearchParser.BaseParsers
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class NumberSqlParserTests
    {
        private readonly NumberSqlParser _parser;

        public NumberSqlParserTests()
        {
            _parser = new NumberSqlParser(ParserTestHelper.CreateMockDefinitionManager());
        }

        [Fact]
        public void GivenSimpleNumber_WhenBuildWhereClause_ThenGeneratesEqConditionWithTypedDecimalParameters()
        {
            // Arrange
            const decimal expectedValue = 42m;
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("42", string.Empty, options);

            // Assert
            Assert.Equal("t.HighValue >= @p0 AND t.LowValue <= @p0", result);
            Assert.DoesNotContain("42", result, StringComparison.Ordinal);
            Assert.Equal(1, command.Parameters.Count);
            AssertTypedDecimalParameter(command.Parameters["@p0"], expectedValue);
        }

        [Fact]
        public void GivenEmptyValue_WhenBuildWhereClause_ThenTreatsItAsZeroEqualityWithTypedParameters()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause(string.Empty, string.Empty, options);

            // Assert
            Assert.Equal("t.HighValue >= @p0 AND t.LowValue <= @p0", result);
            Assert.DoesNotContain("''", result, StringComparison.Ordinal);
            Assert.Equal(1, command.Parameters.Count);
            AssertTypedDecimalParameter(command.Parameters["@p0"], 0m);
        }

        [Fact]
        public void GivenGtPrefix_WhenBuildWhereClause_ThenUsesHighValueGreaterThan()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("gt10", string.Empty, options);

            // Assert
            Assert.Equal("t.HighValue > @p0", result);
            Assert.Equal(1, command.Parameters.Count);
            AssertTypedDecimalParameter(command.Parameters["@p0"], 10m);
        }

        [Fact]
        public void GivenLtPrefix_WhenBuildWhereClause_ThenUsesLowValueLessThan()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("lt10", string.Empty, options);

            // Assert
            Assert.Equal("t.LowValue < @p0", result);
            Assert.Equal(1, command.Parameters.Count);
            AssertTypedDecimalParameter(command.Parameters["@p0"], 10m);
        }

        [Fact]
        public void GivenGePrefix_WhenBuildWhereClause_ThenUsesHighValueGreaterOrEqual()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("ge10", string.Empty, options);

            // Assert
            Assert.Equal("t.HighValue >= @p0", result);
            Assert.Equal(1, command.Parameters.Count);
            AssertTypedDecimalParameter(command.Parameters["@p0"], 10m);
        }

        [Fact]
        public void GivenLePrefix_WhenBuildWhereClause_ThenUsesLowValueLessOrEqual()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("le10", string.Empty, options);

            // Assert
            Assert.Equal("t.LowValue <= @p0", result);
            Assert.Equal(1, command.Parameters.Count);
            AssertTypedDecimalParameter(command.Parameters["@p0"], 10m);
        }

        [Fact]
        public void GivenNePrefix_WhenBuildWhereClause_ThenUsesOrConditionWithTwoDecimalParameters()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("ne10", string.Empty, options);

            // Assert
            Assert.Equal("(t.HighValue > @p0 OR t.LowValue < @p0)", result);
            Assert.Equal(1, command.Parameters.Count);
            AssertTypedDecimalParameter(command.Parameters["@p0"], 10m);
        }

        [Fact]
        public void GivenColumnSuffix_WhenBuildWhereClause_ThenAppendsSuffixToColumnNames()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("gt5", string.Empty, options, columnSuffix: 1);

            // Assert
            Assert.Equal("t.HighValue1 > @p0", result);
        }

        [Fact]
        public void GivenCustomTableName_WhenBuildWhereClause_ThenUsesCustomTableName()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("gt5", string.Empty, options, tableName: "n");

            // Assert
            Assert.Equal("n.HighValue > @p0", result);
        }

        [Fact]
        public void GivenDecimalNumber_WhenBuildWhereClause_ThenHandlesDecimalCorrectly()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("gt3.14", string.Empty, options);

            // Assert
            Assert.Equal("t.HighValue > @p0", result);
            Assert.Equal(1, command.Parameters.Count);
            AssertTypedDecimalParameter(command.Parameters["@p0"], 3.14m);
        }

        [Fact]
        public void GivenNonEnglishCurrentCulture_WhenBuildWhereClause_ThenParsesDecimalUsingInvariantCulture()
        {
            // Arrange
            var previousCulture = CultureInfo.CurrentCulture;
            var previousUiCulture = CultureInfo.CurrentUICulture;
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                CultureInfo.CurrentUICulture = new CultureInfo("de-DE");

                // Act
                var result = _parser.BuildWhereClause("3.14", string.Empty, options);

                // Assert
                Assert.Equal("t.HighValue >= @p0 AND t.LowValue <= @p0", result);
                Assert.Equal(1, command.Parameters.Count);
                AssertTypedDecimalParameter(command.Parameters["@p0"], 3.14m);
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
                CultureInfo.CurrentUICulture = previousUiCulture;
            }
        }

        [Fact]
        public void GivenApPrefix_WhenBuildWhereClause_ThenUsesTypedDecimalParametersForApproximateBounds()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("ap100", string.Empty, options);

            // Assert
            Assert.Equal("(t.HighValue >= @p0 * 0.9 AND t.LowValue <= @p0 * 1.1)", result);
            Assert.DoesNotContain("100", result, StringComparison.Ordinal);
            Assert.Equal(1, command.Parameters.Count);
            AssertTypedDecimalParameter(command.Parameters["@p0"], 100m);
        }

        private static void AssertTypedDecimalParameter(SqlParameter parameter, decimal expectedValue)
        {
            Assert.Equal(SqlDbType.Decimal, parameter.SqlDbType);
            Assert.Equal((byte)36, parameter.Precision);
            Assert.Equal((byte)18, parameter.Scale);
            Assert.IsType<decimal>(parameter.Value);
            Assert.Equal(expectedValue, parameter.Value);
        }
    }
}
