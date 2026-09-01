// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

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
            var result = _parser.BuildWhereClause(string.Empty, string.Empty);

            // Assert
            Assert.Equal("1=1", result);
        }

        [Fact]
        public void GivenValueOnly_WhenBuildWhereClause_ThenGeneratesNumericConditionOnly()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("5.4", string.Empty);

            // Assert — eq is default
            Assert.Contains("t.HighValue", result);
            Assert.Contains("t.LowValue", result);
            Assert.Contains("5.4", result);
            Assert.DoesNotContain("SystemId", result);
            Assert.DoesNotContain("QuantityCodeId", result);
        }

        [Fact]
        public void GivenValueWithSystemAndCode_WhenBuildWhereClause_ThenGeneratesAllThreeConditions()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("5.4|http://unitsofmeasure.org|mg", string.Empty);

            // Assert
            Assert.Contains("t.HighValue", result);
            Assert.Contains("t.LowValue", result);
            Assert.Contains("t.SystemId = (SELECT SystemId FROM dbo.System WHERE Value = 'http://unitsofmeasure.org')", result);
            Assert.Contains("t.QuantityCodeId = (SELECT QuantityCodeId FROM dbo.QuantityCode WHERE Value = 'mg')", result);
        }

        [Fact]
        public void GivenValueWithCodeOnly_WhenBuildWhereClause_ThenGeneratesValueAndCodeConditions()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("5.4||mg", string.Empty);

            // Assert
            Assert.Contains("t.HighValue", result);
            Assert.Contains("t.QuantityCodeId = (SELECT QuantityCodeId FROM dbo.QuantityCode WHERE Value = 'mg')", result);
            Assert.DoesNotContain("SystemId", result);
        }

        [Fact]
        public void GivenGtPrefix_WhenBuildWhereClause_ThenUsesHighValueGreaterThan()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("gt50|http://unitsofmeasure.org|kg", string.Empty);

            // Assert
            Assert.Contains("t.HighValue > 50", result);
            Assert.Contains("t.SystemId", result);
            Assert.Contains("QuantityCodeId", result);
        }

        [Fact]
        public void GivenLePrefix_WhenBuildWhereClause_ThenUsesLowValueLessOrEqual()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("le100.0", string.Empty);

            // Assert
            Assert.Contains("t.LowValue <= 100", result);
        }

        [Fact]
        public void GivenColumnSuffix_WhenBuildWhereClause_ThenAppendsSuffixToColumnNames()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("5.4", string.Empty, columnSuffix: 2);

            // Assert
            Assert.Contains("t.HighValue2", result);
            Assert.Contains("t.LowValue2", result);
        }

        [Fact]
        public void GivenCustomTableName_WhenBuildWhereClause_ThenUsesCustomTableName()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("5.4", string.Empty, tableName: "q");

            // Assert
            Assert.Contains("q.HighValue", result);
        }

        [Fact]
        public void GivenApPrefix_WhenBuildWhereClause_ThenGeneratesApproximateCondition()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("ap100", string.Empty);

            // Assert
            Assert.Contains("0.9", result);
            Assert.Contains("1.1", result);
            Assert.Contains("100", result);
        }
    }
}
