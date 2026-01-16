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
    /// Unit tests for NumberQueryGenerator.
    /// Tests the generator's ability to create SQL queries for number searches.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class NumberQueryGeneratorTests
    {
        private readonly ISqlServerFhirModel _model;
        private readonly SchemaInformation _schemaInformation;

        public NumberQueryGeneratorTests()
        {
            _model = Substitute.For<ISqlServerFhirModel>();
            _schemaInformation = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max);
            _schemaInformation.Current = SchemaVersionConstants.Max;
        }

        [Fact]
        public void GivenNumberQueryGenerator_WhenInstanceAccessed_ThenNotNull()
        {
            Assert.NotNull(NumberQueryGenerator.Instance);
        }

        [Fact]
        public void GivenNumberQueryGenerator_WhenTableAccessed_ThenReturnsNumberSearchParamTable()
        {
            var table = NumberQueryGenerator.Instance.Table;

            Assert.Equal(VLatest.NumberSearchParam.TableName, table.TableName);
        }

        [Theory]
        [InlineData(BinaryOperator.Equal, 123.45)]
        [InlineData(BinaryOperator.GreaterThan, 100.0)]
        [InlineData(BinaryOperator.GreaterThanOrEqual, 50.5)]
        [InlineData(BinaryOperator.LessThan, 200.0)]
        [InlineData(BinaryOperator.LessThanOrEqual, 150.75)]
        public void GivenNumberExpression_WhenVisited_ThenGeneratesCorrectSqlQuery(BinaryOperator binaryOperator, decimal value)
        {
            var expression = new BinaryExpression(binaryOperator, FieldName.Number, null, value);
            var context = CreateContext();

            NumberQueryGenerator.Instance.VisitBinary(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains(VLatest.NumberSearchParam.SingleValue.Metadata.Name, sql);
            Assert.Contains("IS NOT NULL", sql);
            Assert.Contains(GetOperatorString(binaryOperator), sql);
            Assert.NotEmpty(sql);
            Assert.True(context.Parameters.HasParametersToHash);
        }

        [Fact]
        public void GivenNumberSingleValueExpression_WhenVisited_ThenChecksNotNull()
        {
            var expression = new BinaryExpression(BinaryOperator.Equal, FieldName.Number, null, 123.45m);
            var context = CreateContext();

            NumberQueryGenerator.Instance.VisitBinary(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains(VLatest.NumberSearchParam.SingleValue.Metadata.Name, sql);
            Assert.Contains("IS NOT NULL", sql);
            Assert.Contains("AND", sql);
        }

        [Theory]
        [InlineData(BinaryOperator.Equal)]
        [InlineData(BinaryOperator.GreaterThan)]
        [InlineData(BinaryOperator.LessThan)]
        public void GivenNumberLowExpression_WhenVisited_ThenGeneratesCorrectSqlQuery(BinaryOperator binaryOperator)
        {
            var expression = new BinaryExpression(binaryOperator, SqlFieldName.NumberLow, null, 50.0m);
            var context = CreateContext();

            NumberQueryGenerator.Instance.VisitBinary(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains(VLatest.NumberSearchParam.LowValue.Metadata.Name, sql);
            Assert.DoesNotContain("IS NOT NULL", sql); // LowValue is not nullable
            Assert.Contains(GetOperatorString(binaryOperator), sql);
        }

        [Theory]
        [InlineData(BinaryOperator.Equal)]
        [InlineData(BinaryOperator.GreaterThan)]
        [InlineData(BinaryOperator.LessThan)]
        public void GivenNumberHighExpression_WhenVisited_ThenGeneratesCorrectSqlQuery(BinaryOperator binaryOperator)
        {
            var expression = new BinaryExpression(binaryOperator, SqlFieldName.NumberHigh, null, 100.0m);
            var context = CreateContext();

            NumberQueryGenerator.Instance.VisitBinary(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains(VLatest.NumberSearchParam.HighValue.Metadata.Name, sql);
            Assert.DoesNotContain("IS NOT NULL", sql); // HighValue is not nullable
            Assert.Contains(GetOperatorString(binaryOperator), sql);
        }

        [Fact]
        public void GivenNumberExpressionWithComponentIndex_WhenVisited_ThenIncludesComponentIndex()
        {
            var expression = new BinaryExpression(BinaryOperator.Equal, FieldName.Number, 0, 123.45m);
            var context = CreateContext();

            NumberQueryGenerator.Instance.VisitBinary(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains(VLatest.NumberSearchParam.SingleValue.Metadata.Name + "1", sql);
        }

        [Fact]
        public void GivenNumberExpressionWithTableAlias_WhenVisited_ThenSqlContainsTableAlias()
        {
            const string tableAlias = "num";
            var expression = new BinaryExpression(BinaryOperator.Equal, FieldName.Number, null, 123.45m);
            var context = CreateContext(tableAlias);

            NumberQueryGenerator.Instance.VisitBinary(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains($"{tableAlias}.{VLatest.NumberSearchParam.SingleValue.Metadata.Name}", sql);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(-1)]
        [InlineData(100)]
        [InlineData(-100)]
        public void GivenIntegerNumbers_WhenVisited_ThenGeneratesSQL(int value)
        {
            var expression = new BinaryExpression(BinaryOperator.Equal, FieldName.Number, null, value);
            var context = CreateContext();

            NumberQueryGenerator.Instance.VisitBinary(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.NotEmpty(sql);
            Assert.Contains(VLatest.NumberSearchParam.SingleValue.Metadata.Name, sql);
            Assert.True(context.Parameters.HasParametersToHash);
        }

        [Theory]
        [InlineData(0.1)]
        [InlineData(123.456)]
        [InlineData(-456.789)]
        [InlineData(999.999)]
        public void GivenDecimalNumbers_WhenVisited_ThenGeneratesSQL(decimal value)
        {
            var expression = new BinaryExpression(BinaryOperator.Equal, FieldName.Number, null, value);
            var context = CreateContext();

            NumberQueryGenerator.Instance.VisitBinary(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.NotEmpty(sql);
            Assert.Contains(VLatest.NumberSearchParam.SingleValue.Metadata.Name, sql);
            Assert.True(context.Parameters.HasParametersToHash);
        }

        [Fact]
        public void GivenVeryLargeNumber_WhenVisited_ThenHandlesCorrectly()
        {
            var largeNumber = 999999999999999999.999999999999m;
            var expression = new BinaryExpression(BinaryOperator.Equal, FieldName.Number, null, largeNumber);
            var context = CreateContext();

            NumberQueryGenerator.Instance.VisitBinary(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.NotEmpty(sql);
            Assert.Contains(VLatest.NumberSearchParam.SingleValue.Metadata.Name, sql);
        }

        [Fact]
        public void GivenVerySmallNumber_WhenVisited_ThenHandlesCorrectly()
        {
            var smallNumber = 0.000000000000000001m;
            var expression = new BinaryExpression(BinaryOperator.Equal, FieldName.Number, null, smallNumber);
            var context = CreateContext();

            NumberQueryGenerator.Instance.VisitBinary(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.NotEmpty(sql);
            Assert.Contains(VLatest.NumberSearchParam.SingleValue.Metadata.Name, sql);
        }

        [Fact]
        public void GivenNegativeNumber_WhenVisited_ThenHandlesCorrectly()
        {
            var negativeNumber = -123.45m;
            var expression = new BinaryExpression(BinaryOperator.LessThan, FieldName.Number, null, negativeNumber);
            var context = CreateContext();

            NumberQueryGenerator.Instance.VisitBinary(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains("<", sql);
            Assert.Contains(VLatest.NumberSearchParam.SingleValue.Metadata.Name, sql);
        }

        [Fact]
        public void GivenInvalidFieldName_WhenVisited_ThenThrowsArgumentOutOfRangeException()
        {
            var expression = new BinaryExpression(BinaryOperator.Equal, FieldName.TokenCode, null, 123);
            var context = CreateContext();

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                NumberQueryGenerator.Instance.VisitBinary(expression, context));
        }

        [Fact]
        public void GivenMultipleNumberExpressions_WhenVisited_ThenEachGeneratesSQL()
        {
            var expression1 = new BinaryExpression(BinaryOperator.GreaterThan, FieldName.Number, null, 100m);
            var expression2 = new BinaryExpression(BinaryOperator.LessThan, SqlFieldName.NumberHigh, null, 200m);

            var context1 = CreateContext();
            var context2 = CreateContext();

            NumberQueryGenerator.Instance.VisitBinary(expression1, context1);
            NumberQueryGenerator.Instance.VisitBinary(expression2, context2);

            var sql1 = context1.StringBuilder.ToString();
            var sql2 = context2.StringBuilder.ToString();

            Assert.Contains(VLatest.NumberSearchParam.SingleValue.Metadata.Name, sql1);
            Assert.Contains(">", sql1);

            Assert.Contains(VLatest.NumberSearchParam.HighValue.Metadata.Name, sql2);
            Assert.Contains("<", sql2);

            Assert.True(context1.Parameters.HasParametersToHash);
            Assert.True(context2.Parameters.HasParametersToHash);
        }

        [Fact]
        public void GivenNumberRangeQuery_WhenUsingLowAndHigh_ThenGeneratesCorrectSQL()
        {
            var lowExpression = new BinaryExpression(BinaryOperator.GreaterThanOrEqual, SqlFieldName.NumberLow, null, 50m);
            var highExpression = new BinaryExpression(BinaryOperator.LessThanOrEqual, SqlFieldName.NumberHigh, null, 150m);

            var contextLow = CreateContext();
            var contextHigh = CreateContext();

            NumberQueryGenerator.Instance.VisitBinary(lowExpression, contextLow);
            NumberQueryGenerator.Instance.VisitBinary(highExpression, contextHigh);

            var sqlLow = contextLow.StringBuilder.ToString();
            var sqlHigh = contextHigh.StringBuilder.ToString();

            Assert.Contains(VLatest.NumberSearchParam.LowValue.Metadata.Name, sqlLow);
            Assert.Contains(">=", sqlLow);

            Assert.Contains(VLatest.NumberSearchParam.HighValue.Metadata.Name, sqlHigh);
            Assert.Contains("<=", sqlHigh);
        }

        [Theory]
        [InlineData(BinaryOperator.NotEqual)]
        public void GivenNotEqualOperator_WhenVisited_ThenGeneratesCorrectSQL(BinaryOperator binaryOperator)
        {
            var expression = new BinaryExpression(binaryOperator, FieldName.Number, null, 100m);
            var context = CreateContext();

            NumberQueryGenerator.Instance.VisitBinary(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains("<>", sql);
            Assert.Contains(VLatest.NumberSearchParam.SingleValue.Metadata.Name, sql);
        }

        [Fact]
        public void GivenZeroValue_WhenVisited_ThenHandlesCorrectly()
        {
            var expression = new BinaryExpression(BinaryOperator.Equal, FieldName.Number, null, 0m);
            var context = CreateContext();

            NumberQueryGenerator.Instance.VisitBinary(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.NotEmpty(sql);
            Assert.Contains(VLatest.NumberSearchParam.SingleValue.Metadata.Name, sql);
        }

        private SearchParameterQueryGeneratorContext CreateContext(string tableAlias = null)
        {
            var stringBuilder = new IndentedStringBuilder(new StringBuilder());
            var sqlCommand = new SqlCommand();
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
