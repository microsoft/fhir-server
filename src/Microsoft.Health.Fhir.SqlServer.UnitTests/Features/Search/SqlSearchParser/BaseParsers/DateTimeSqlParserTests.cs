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
    public class DateTimeSqlParserTests
    {
        private readonly DateTimeSqlParser _parser;

        public DateTimeSqlParserTests()
        {
            _parser = new DateTimeSqlParser(ParserTestHelper.CreateMockDefinitionManager());
        }

        [Fact]
        public void GivenExactDate_WhenBuildWhereClause_ThenUsesEqModifier()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("2024-01-15", string.Empty);

            // Assert — default is eq, so produces range overlap check
            Assert.Contains("t.EndDateTime", result);
            Assert.Contains("t.StartDateTime", result);
            Assert.Contains("2024-01-15", result);
        }

        [Fact]
        public void GivenYearOnly_WhenBuildWhereClause_ThenProducesRangeForWholeYear()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("2024", string.Empty);

            // Assert
            Assert.Contains("2024", result);
            Assert.Contains("t.EndDateTime", result);
            Assert.Contains("t.StartDateTime", result);
        }

        [Fact]
        public void GivenGtPrefix_WhenBuildWhereClause_ThenUsesEndDateTimeGreaterThan()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("gt2024-01-15", string.Empty);

            // Assert
            Assert.Contains("t.EndDateTime", result);
            Assert.Contains(">", result);
            Assert.DoesNotContain("<=", result);
        }

        [Fact]
        public void GivenLtPrefix_WhenBuildWhereClause_ThenUsesStartDateTimeLessThan()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("lt2024-01-15", string.Empty);

            // Assert
            Assert.Contains("t.StartDateTime", result);
            Assert.Contains("<", result);
        }

        [Fact]
        public void GivenGePrefix_WhenBuildWhereClause_ThenUsesEndDateTimeGreaterOrEqual()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("ge2024-01-15", string.Empty);

            // Assert
            Assert.Contains("t.EndDateTime", result);
            Assert.Contains(">=", result);
        }

        [Fact]
        public void GivenLePrefix_WhenBuildWhereClause_ThenUsesStartDateTimeLessOrEqual()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("le2024-01-15", string.Empty);

            // Assert
            Assert.Contains("t.StartDateTime", result);
            Assert.Contains("<=", result);
        }

        [Fact]
        public void GivenNePrefix_WhenBuildWhereClause_ThenUsesOrCondition()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("ne2024-01-15", string.Empty);

            // Assert
            Assert.Contains("OR", result);
            Assert.Contains("t.EndDateTime", result);
            Assert.Contains("t.StartDateTime", result);
        }

        [Fact]
        public void GivenSaPrefix_WhenBuildWhereClause_ThenUsesStartDateTimeGreaterThan()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("sa2024-01-15", string.Empty);

            // Assert
            Assert.Contains("t.StartDateTime", result);
            Assert.Contains(">", result);
        }

        [Fact]
        public void GivenEbPrefix_WhenBuildWhereClause_ThenUsesEndDateTimeLessThan()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("eb2024-01-15", string.Empty);

            // Assert
            Assert.Contains("t.EndDateTime", result);
            Assert.Contains("<", result);
        }

        [Fact]
        public void GivenDateWithTime_WhenBuildWhereClause_ThenIncludesTimeInCondition()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("2024-01-15T10:30:00Z", string.Empty);

            // Assert
            Assert.Contains("2024-01-15T10:30:00", result);
        }

        [Fact]
        public void GivenColumnSuffix_WhenBuildWhereClause_ThenAppendsSuffix()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("gt2024-01-15", string.Empty, columnSuffix: 2);

            // Assert
            Assert.Contains("t.EndDateTime2", result);
        }

        [Fact]
        public void GivenCustomTableName_WhenBuildWhereClause_ThenUsesCustomTableName()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("gt2024-01-15", string.Empty, tableName: "dt");

            // Assert
            Assert.Contains("dt.EndDateTime", result);
        }
    }
}
