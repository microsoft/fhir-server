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
    public class NumberSqlParserTests
    {
        private readonly NumberSqlParser _parser;

        public NumberSqlParserTests()
        {
            _parser = new NumberSqlParser(ParserTestHelper.CreateMockDefinitionManager());
        }

        [Fact]
        public void GivenSimpleNumber_WhenBuildWhereClause_ThenGeneratesEqCondition()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("42", string.Empty);

            // Assert — eq: HighValue >= val AND LowValue <= val
            Assert.Contains("t.HighValue", result);
            Assert.Contains("t.LowValue", result);
            Assert.Contains("42", result);
        }

        [Fact]
        public void GivenGtPrefix_WhenBuildWhereClause_ThenUsesHighValueGreaterThan()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("gt10", string.Empty);

            // Assert
            Assert.Contains("t.HighValue > 10", result);
        }

        [Fact]
        public void GivenLtPrefix_WhenBuildWhereClause_ThenUsesLowValueLessThan()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("lt10", string.Empty);

            // Assert
            Assert.Contains("t.LowValue < 10", result);
        }

        [Fact]
        public void GivenGePrefix_WhenBuildWhereClause_ThenUsesHighValueGreaterOrEqual()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("ge10", string.Empty);

            // Assert
            Assert.Contains("t.HighValue >= 10", result);
        }

        [Fact]
        public void GivenLePrefix_WhenBuildWhereClause_ThenUsesLowValueLessOrEqual()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("le10", string.Empty);

            // Assert
            Assert.Contains("t.LowValue <= 10", result);
        }

        [Fact]
        public void GivenNePrefix_WhenBuildWhereClause_ThenUsesOrCondition()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("ne10", string.Empty);

            // Assert
            Assert.Contains("OR", result);
            Assert.Contains("t.HighValue", result);
            Assert.Contains("t.LowValue", result);
        }

        [Fact]
        public void GivenColumnSuffix_WhenBuildWhereClause_ThenAppendsSuffixToColumnNames()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("gt5", string.Empty, columnSuffix: 1);

            // Assert
            Assert.Contains("t.HighValue1 > 5", result);
        }

        [Fact]
        public void GivenCustomTableName_WhenBuildWhereClause_ThenUsesCustomTableName()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("gt5", string.Empty, tableName: "n");

            // Assert
            Assert.Contains("n.HighValue > 5", result);
        }

        [Fact]
        public void GivenDecimalNumber_WhenBuildWhereClause_ThenHandlesDecimalCorrectly()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("gt3.14", string.Empty);

            // Assert
            Assert.Contains("3.14", result);
        }
    }
}
