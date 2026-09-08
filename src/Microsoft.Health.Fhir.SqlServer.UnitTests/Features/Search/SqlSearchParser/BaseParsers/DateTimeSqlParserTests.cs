// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
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
        public void GivenExactDate_WhenBuildWhereClause_ThenUsesTypedBoundaryParameters()
        {
            // Arrange
            const string value = "2024-01-15";
            DateTimeSearchValue parsedValue = DateTimeSearchValue.Parse(value);
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause(value, string.Empty, options);

            // Assert
            Assert.Equal("t.EndDateTime >= @p0 AND t.StartDateTime <= @p1", result);
            Assert.DoesNotContain(value, result, StringComparison.Ordinal);
            Assert.Equal(2, command.Parameters.Count);
            Assert.IsType<DateTimeOffset>(command.Parameters["@p0"].Value);
            Assert.IsType<DateTimeOffset>(command.Parameters["@p1"].Value);
            Assert.Equal(parsedValue.Start, command.Parameters["@p0"].Value);
            Assert.Equal(parsedValue.End, command.Parameters["@p1"].Value);
        }

        [Fact]
        public void GivenYearOnly_WhenBuildWhereClause_ThenProducesRangeForWholeYearWithParameters()
        {
            // Arrange
            const string value = "2024";
            DateTimeSearchValue parsedValue = DateTimeSearchValue.Parse(value);
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause(value, string.Empty, options);

            // Assert
            Assert.Equal("t.EndDateTime >= @p0 AND t.StartDateTime <= @p1", result);
            Assert.DoesNotContain(value, result, StringComparison.Ordinal);
            Assert.Equal(parsedValue.Start, command.Parameters["@p0"].Value);
            Assert.Equal(parsedValue.End, command.Parameters["@p1"].Value);
        }

        [Fact]
        public void GivenGtPrefix_WhenBuildWhereClause_ThenUsesEndDateTimeGreaterThan()
        {
            // Arrange
            const string value = "gt2024-01-15";
            var parsedValue = DateTimeSqlParser.ParseValue(value, out _);
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause(value, string.Empty, options);

            // Assert
            Assert.Equal("t.EndDateTime > @p0", result);
            Assert.Equal(parsedValue.End, command.Parameters["@p0"].Value);
        }

        [Fact]
        public void GivenLtPrefix_WhenBuildWhereClause_ThenUsesStartDateTimeLessThan()
        {
            // Arrange
            const string value = "lt2024-01-15";
            var parsedValue = DateTimeSqlParser.ParseValue(value, out _);
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause(value, string.Empty, options);

            // Assert
            Assert.Equal("t.StartDateTime < @p0", result);
            Assert.Equal(parsedValue.Start, command.Parameters["@p0"].Value);
        }

        [Fact]
        public void GivenGePrefix_WhenBuildWhereClause_ThenUsesEndDateTimeGreaterOrEqual()
        {
            // Arrange
            const string value = "ge2024-01-15";
            var parsedValue = DateTimeSqlParser.ParseValue(value, out _);
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause(value, string.Empty, options);

            // Assert
            Assert.Equal("t.EndDateTime >= @p0", result);
            Assert.Equal(parsedValue.Start, command.Parameters["@p0"].Value);
        }

        [Fact]
        public void GivenLePrefix_WhenBuildWhereClause_ThenUsesStartDateTimeLessOrEqual()
        {
            // Arrange
            const string value = "le2024-01-15";
            var parsedValue = DateTimeSqlParser.ParseValue(value, out _);
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause(value, string.Empty, options);

            // Assert
            Assert.Equal("t.StartDateTime <= @p0", result);
            Assert.Equal(parsedValue.End, command.Parameters["@p0"].Value);
        }

        [Fact]
        public void GivenNePrefix_WhenBuildWhereClause_ThenUsesOrConditionWithTwoBoundaryParameters()
        {
            // Arrange
            const string value = "ne2024-01-15";
            var parsedValue = DateTimeSqlParser.ParseValue(value, out _);
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause(value, string.Empty, options);

            // Assert
            Assert.Equal("(t.EndDateTime > @p0 OR t.StartDateTime < @p1)", result);
            Assert.Equal(2, command.Parameters.Count);
            Assert.Equal(parsedValue.End, command.Parameters["@p0"].Value);
            Assert.Equal(parsedValue.Start, command.Parameters["@p1"].Value);
        }

        [Fact]
        public void GivenSaPrefix_WhenBuildWhereClause_ThenUsesStartDateTimeGreaterThan()
        {
            // Arrange
            const string value = "sa2024-01-15";
            var parsedValue = DateTimeSqlParser.ParseValue(value, out _);
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause(value, string.Empty, options);

            // Assert
            Assert.Equal("t.StartDateTime > @p0", result);
            Assert.Equal(parsedValue.End, command.Parameters["@p0"].Value);
        }

        [Fact]
        public void GivenEbPrefix_WhenBuildWhereClause_ThenUsesEndDateTimeLessThan()
        {
            // Arrange
            const string value = "eb2024-01-15";
            var parsedValue = DateTimeSqlParser.ParseValue(value, out _);
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause(value, string.Empty, options);

            // Assert
            Assert.Equal("t.EndDateTime < @p0", result);
            Assert.Equal(parsedValue.Start, command.Parameters["@p0"].Value);
        }

        [Fact]
        public void GivenDateWithTime_WhenBuildWhereClause_ThenBindsDateTimeRange()
        {
            // Arrange
            const string value = "2024-01-15T10:30:00Z";
            DateTimeSearchValue parsedValue = DateTimeSearchValue.Parse(value);
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause(value, string.Empty, options);

            // Assert
            Assert.Equal("t.EndDateTime >= @p0 AND t.StartDateTime <= @p1", result);
            Assert.DoesNotContain("2024-01-15T10:30:00", result, StringComparison.Ordinal);
            Assert.Equal(parsedValue.Start, command.Parameters["@p0"].Value);
            Assert.Equal(parsedValue.End, command.Parameters["@p1"].Value);
        }

        [Fact]
        public void GivenColumnSuffix_WhenBuildWhereClause_ThenAppendsSuffix()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("gt2024-01-15", string.Empty, options, columnSuffix: 2);

            // Assert
            Assert.Equal("t.EndDateTime2 > @p0", result);
        }

        [Fact]
        public void GivenCustomTableName_WhenBuildWhereClause_ThenUsesCustomTableName()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("gt2024-01-15", string.Empty, options, tableName: "dt");

            // Assert
            Assert.Equal("dt.EndDateTime > @p0", result);
        }
    }
}
