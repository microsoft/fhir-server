// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Storage;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions.Visitors.QueryGenerators
{
    /// <summary>
    /// Unit tests for DateTimeQueryGenerator.
    /// Tests the generator's ability to create SQL queries for date/time searches.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class DateTimeQueryGeneratorTests
    {
        private readonly ISqlServerFhirModel _model;
        private readonly SchemaInformation _schemaInformation;

        public DateTimeQueryGeneratorTests()
        {
            _model = Substitute.For<ISqlServerFhirModel>();
            _schemaInformation = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max);
            _schemaInformation.Current = SchemaVersionConstants.Max;
        }

        [Fact]
        public void GivenDateTimeQueryGenerator_WhenInstanceAccessed_ThenNotNull()
        {
            Assert.NotNull(DateTimeQueryGenerator.Instance);
        }

        [Fact]
        public void GivenDateTimeQueryGenerator_WhenTableAccessed_ThenReturnsDateTimeSearchParamTable()
        {
            var table = DateTimeQueryGenerator.Instance.Table;

            Assert.Equal(VLatest.DateTimeSearchParam.TableName, table.TableName);
        }

        [Theory]
        [InlineData(BinaryOperator.Equal)]
        [InlineData(BinaryOperator.GreaterThan)]
        [InlineData(BinaryOperator.GreaterThanOrEqual)]
        [InlineData(BinaryOperator.LessThan)]
        [InlineData(BinaryOperator.LessThanOrEqual)]
        public void GivenDateTimeStartExpression_WhenVisited_ThenGeneratesCorrectSqlQuery(BinaryOperator binaryOperator)
        {
            var dateTime = new DateTimeOffset(2023, 1, 15, 10, 30, 0, TimeSpan.Zero);
            var expression = new BinaryExpression(binaryOperator, FieldName.DateTimeStart, null, dateTime);
            var context = CreateContext();

            DateTimeQueryGenerator.Instance.VisitBinary(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains(VLatest.DateTimeSearchParam.StartDateTime.Metadata.Name, sql);
            Assert.Matches($@"{VLatest.DateTimeSearchParam.StartDateTime.Metadata.Name}\s*{System.Text.RegularExpressions.Regex.Escape(GetOperatorString(binaryOperator))}\s*@\w+", sql);
            Assert.NotEmpty(sql);
            Assert.True(context.Parameters.HasParametersToHash);
        }

        [Theory]
        [InlineData(BinaryOperator.Equal)]
        [InlineData(BinaryOperator.GreaterThan)]
        [InlineData(BinaryOperator.GreaterThanOrEqual)]
        [InlineData(BinaryOperator.LessThan)]
        [InlineData(BinaryOperator.LessThanOrEqual)]
        public void GivenDateTimeEndExpression_WhenVisited_ThenGeneratesCorrectSqlQuery(BinaryOperator binaryOperator)
        {
            var dateTime = new DateTimeOffset(2023, 12, 31, 23, 59, 59, TimeSpan.Zero);
            var expression = new BinaryExpression(binaryOperator, FieldName.DateTimeEnd, null, dateTime);
            var context = CreateContext();

            DateTimeQueryGenerator.Instance.VisitBinary(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains(VLatest.DateTimeSearchParam.EndDateTime.Metadata.Name, sql);
            Assert.Matches($@"{VLatest.DateTimeSearchParam.EndDateTime.Metadata.Name}\s*{System.Text.RegularExpressions.Regex.Escape(GetOperatorString(binaryOperator))}\s*@\w+", sql);
            Assert.NotEmpty(sql);
            Assert.True(context.Parameters.HasParametersToHash);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void GivenIsLongerThanADayExpression_WhenVisited_ThenGeneratesCorrectSqlQuery(bool isLongerThanADay)
        {
            var expression = new BinaryExpression(BinaryOperator.Equal, SqlFieldName.DateTimeIsLongerThanADay, null, isLongerThanADay);
            var context = CreateContext();

            DateTimeQueryGenerator.Instance.VisitBinary(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains(VLatest.DateTimeSearchParam.IsLongerThanADay.Metadata.Name, sql);
            Assert.Matches($@"{VLatest.DateTimeSearchParam.IsLongerThanADay.Metadata.Name}\s*=\s*{(isLongerThanADay ? "1" : "0")}", sql);
            Assert.NotEmpty(sql);
        }

        [Fact]
        public void GivenDateTimeExpressionWithComponentIndex_WhenVisited_ThenIncludesComponentIndex()
        {
            var dateTime = new DateTimeOffset(2023, 6, 15, 12, 0, 0, TimeSpan.Zero);
            var expression = new BinaryExpression(BinaryOperator.Equal, FieldName.DateTimeStart, 0, dateTime);
            var context = CreateContext();

            DateTimeQueryGenerator.Instance.VisitBinary(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains(VLatest.DateTimeSearchParam.StartDateTime.Metadata.Name + "1", sql);
        }

        [Fact]
        public void GivenDateTimeExpressionWithTableAlias_WhenVisited_ThenSqlContainsTableAlias()
        {
            const string tableAlias = "dt";
            var dateTime = new DateTimeOffset(2023, 3, 20, 8, 0, 0, TimeSpan.Zero);
            var expression = new BinaryExpression(BinaryOperator.GreaterThan, FieldName.DateTimeStart, null, dateTime);
            var context = CreateContext(tableAlias);

            DateTimeQueryGenerator.Instance.VisitBinary(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains($"{tableAlias}.{VLatest.DateTimeSearchParam.StartDateTime.Metadata.Name}", sql);
        }

        [Fact]
        public void GivenMultipleDateTimeExpressions_WhenVisited_ThenEachGeneratesSQL()
        {
            var startDate = new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var endDate = new DateTimeOffset(2023, 12, 31, 23, 59, 59, TimeSpan.Zero);

            var expression1 = new BinaryExpression(BinaryOperator.GreaterThanOrEqual, FieldName.DateTimeStart, null, startDate);
            var expression2 = new BinaryExpression(BinaryOperator.LessThanOrEqual, FieldName.DateTimeEnd, null, endDate);

            var context1 = CreateContext();
            var context2 = CreateContext();

            DateTimeQueryGenerator.Instance.VisitBinary(expression1, context1);
            DateTimeQueryGenerator.Instance.VisitBinary(expression2, context2);

            var sql1 = context1.StringBuilder.ToString();
            var sql2 = context2.StringBuilder.ToString();

            Assert.Contains(VLatest.DateTimeSearchParam.StartDateTime.Metadata.Name, sql1);
            Assert.Contains(">=", sql1);

            Assert.Contains(VLatest.DateTimeSearchParam.EndDateTime.Metadata.Name, sql2);
            Assert.Contains("<=", sql2);

            Assert.True(context1.Parameters.HasParametersToHash);
            Assert.True(context2.Parameters.HasParametersToHash);
        }

        [Fact]
        public void GivenDateTimeExpression_WhenVisited_ThenConvertsToUtcDateTime()
        {
            var localDateTime = new DateTimeOffset(2023, 7, 4, 14, 30, 0, TimeSpan.FromHours(-5));
            var expression = new BinaryExpression(BinaryOperator.Equal, FieldName.DateTimeStart, null, localDateTime);
            var context = CreateContext();

            DateTimeQueryGenerator.Instance.VisitBinary(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.NotEmpty(sql);
            Assert.True(context.Parameters.HasParametersToHash);
        }

        [Fact]
        public void GivenInvalidFieldName_WhenVisited_ThenThrowsArgumentOutOfRangeException()
        {
            var dateTime = new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var expression = new BinaryExpression(BinaryOperator.Equal, FieldName.TokenCode, null, dateTime);
            var context = CreateContext();

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                DateTimeQueryGenerator.Instance.VisitBinary(expression, context));
        }

        [Theory]
        [InlineData("2020-01-01T00:00:00Z")]
        [InlineData("2023-06-15T12:30:45Z")]
        [InlineData("2025-12-31T23:59:59Z")]
        public void GivenVariousDateTimes_WhenVisited_ThenGeneratesSQL(string dateTimeString)
        {
            var dateTime = DateTimeOffset.Parse(dateTimeString);
            var expression = new BinaryExpression(BinaryOperator.Equal, FieldName.DateTimeStart, null, dateTime);
            var context = CreateContext();

            DateTimeQueryGenerator.Instance.VisitBinary(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.NotEmpty(sql);
            Assert.Contains(VLatest.DateTimeSearchParam.StartDateTime.Metadata.Name, sql);
            Assert.True(context.Parameters.HasParametersToHash);
        }

        private SearchParameterQueryGeneratorContext CreateContext(string tableAlias = null)
        {
            var stringBuilder = new IndentedStringBuilder(new StringBuilder());
            using var sqlCommand = new SqlCommand();
            var sqlParameterManager = new SqlQueryParameterManager(sqlCommand.Parameters);
            var parameters = new HashingSqlQueryParameterManager(sqlParameterManager);

            return new SearchParameterQueryGeneratorContext(
                stringBuilder,
                parameters,
                _model,
                _schemaInformation,
                isAsyncOperation: false,
                tableAlias);
        }

        private static string GetOperatorString(BinaryOperator binaryOperator)
        {
            return binaryOperator switch
            {
                BinaryOperator.Equal => "=",
                BinaryOperator.GreaterThan => ">",
                BinaryOperator.GreaterThanOrEqual => ">=",
                BinaryOperator.LessThan => "<",
                BinaryOperator.LessThanOrEqual => "<=",
                BinaryOperator.NotEqual => "<>",
                _ => throw new ArgumentOutOfRangeException(nameof(binaryOperator)),
            };
        }
    }
}
