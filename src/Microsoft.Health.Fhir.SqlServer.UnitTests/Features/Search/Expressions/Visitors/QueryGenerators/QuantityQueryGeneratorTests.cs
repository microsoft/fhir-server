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
    /// Unit tests for QuantityQueryGenerator.
    /// Tests the generator's ability to create SQL queries for quantity searches.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class QuantityQueryGeneratorTests
    {
        private readonly ISqlServerFhirModel _model;
        private readonly SchemaInformation _schemaInformation;

        public QuantityQueryGeneratorTests()
        {
            _model = Substitute.For<ISqlServerFhirModel>();
            _schemaInformation = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max);
            _schemaInformation.Current = SchemaVersionConstants.Max;
        }

        [Fact]
        public void GivenQuantityQueryGenerator_WhenInstanceAccessed_ThenNotNull()
        {
            Assert.NotNull(QuantityQueryGenerator.Instance);
        }

        [Fact]
        public void GivenQuantityQueryGenerator_WhenTableAccessed_ThenReturnsQuantitySearchParamTable()
        {
            var table = QuantityQueryGenerator.Instance.Table;

            Assert.Equal(VLatest.QuantitySearchParam.TableName, table.TableName);
        }

        [Theory]
        [InlineData(BinaryOperator.Equal, 5.4)]
        [InlineData(BinaryOperator.GreaterThan, 10.0)]
        [InlineData(BinaryOperator.LessThan, 20.0)]
        public void GivenQuantitySingleValueExpression_WhenVisited_ThenGeneratesCorrectSqlQuery(BinaryOperator binaryOperator, decimal value)
        {
            var expression = new BinaryExpression(binaryOperator, FieldName.Quantity, null, value);
            var context = CreateContext();

            QuantityQueryGenerator.Instance.VisitBinary(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains(VLatest.QuantitySearchParam.SingleValue.Metadata.Name, sql);
            Assert.Contains("IS NOT NULL", sql);
            Assert.Contains(GetOperatorString(binaryOperator), sql);
            Assert.True(context.Parameters.HasParametersToHash);
        }

        [Fact]
        public void GivenQuantityLowValueExpression_WhenVisited_ThenGeneratesCorrectSqlQuery()
        {
            var expression = new BinaryExpression(BinaryOperator.GreaterThanOrEqual, SqlFieldName.QuantityLow, null, 5.0m);
            var context = CreateContext();

            QuantityQueryGenerator.Instance.VisitBinary(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains(VLatest.QuantitySearchParam.LowValue.Metadata.Name, sql);
            Assert.DoesNotContain("IS NOT NULL", sql);
            Assert.Contains(">=", sql);
        }

        [Fact]
        public void GivenQuantityHighValueExpression_WhenVisited_ThenGeneratesCorrectSqlQuery()
        {
            var expression = new BinaryExpression(BinaryOperator.LessThanOrEqual, SqlFieldName.QuantityHigh, null, 100.0m);
            var context = CreateContext();

            QuantityQueryGenerator.Instance.VisitBinary(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains(VLatest.QuantitySearchParam.HighValue.Metadata.Name, sql);
            Assert.DoesNotContain("IS NOT NULL", sql);
            Assert.Contains("<=", sql);
        }

        [Fact]
        public void GivenQuantityCodeExpression_WhenCodeIdExists_ThenUsesDirectComparison()
        {
            const string codeValue = "mg";
            const int quantityCodeId = 1;

            _model.TryGetQuantityCodeId(codeValue, out Arg.Any<int>())
                .Returns(x =>
                {
                    x[1] = quantityCodeId;
                    return true;
                });

            var expression = new StringExpression(StringOperator.Equals, FieldName.QuantityCode, null, codeValue, true);
            var context = CreateContext();

            QuantityQueryGenerator.Instance.VisitString(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains(VLatest.QuantitySearchParam.QuantityCodeId.Metadata.Name, sql);
            Assert.Contains("=", sql);
        }

        [Fact]
        public void GivenQuantityCodeExpression_WhenCodeIdNotExists_ThenUsesSubquery()
        {
            const string codeValue = "custom-unit";

            _model.TryGetQuantityCodeId(codeValue, out Arg.Any<int>())
                .Returns(false);

            var expression = new StringExpression(StringOperator.Equals, FieldName.QuantityCode, null, codeValue, true);
            var context = CreateContext();

            QuantityQueryGenerator.Instance.VisitString(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains(VLatest.QuantitySearchParam.QuantityCodeId.Metadata.Name, sql);
            Assert.Contains("IN", sql);
            Assert.Contains("SELECT", sql);
            Assert.Contains(VLatest.QuantityCode.TableName, sql);
        }

        [Fact]
        public void GivenQuantitySystemExpression_WhenSystemIdExists_ThenUsesDirectComparison()
        {
            const string systemValue = "http://unitsofmeasure.org";
            const int systemId = 1;

            _model.TryGetSystemId(systemValue, out Arg.Any<int>())
                .Returns(x =>
                {
                    x[1] = systemId;
                    return true;
                });

            var expression = new StringExpression(StringOperator.Equals, FieldName.QuantitySystem, null, systemValue, true);
            var context = CreateContext();

            QuantityQueryGenerator.Instance.VisitString(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains(VLatest.QuantitySearchParam.SystemId.Metadata.Name, sql);
            Assert.Contains("=", sql);
        }

        [Fact]
        public void GivenQuantitySystemExpression_WhenSystemIdNotExists_ThenUsesSubquery()
        {
            const string systemValue = "http://custom-system.org";

            _model.TryGetSystemId(systemValue, out Arg.Any<int>())
                .Returns(false);

            var expression = new StringExpression(StringOperator.Equals, FieldName.QuantitySystem, null, systemValue, true);
            var context = CreateContext();

            QuantityQueryGenerator.Instance.VisitString(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains(VLatest.QuantitySearchParam.SystemId.Metadata.Name, sql);
            Assert.Contains("IN", sql);
            Assert.Contains("SELECT", sql);
            Assert.Contains(VLatest.System.TableName, sql);
        }

        [Fact]
        public void GivenQuantityExpressionWithComponentIndex_WhenVisited_ThenIncludesComponentIndex()
        {
            var expression = new BinaryExpression(BinaryOperator.Equal, FieldName.Quantity, 0, 5.4m);
            var context = CreateContext();

            QuantityQueryGenerator.Instance.VisitBinary(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains(VLatest.QuantitySearchParam.SingleValue.Metadata.Name + "1", sql);
        }

        [Fact]
        public void GivenQuantityExpressionWithTableAlias_WhenVisited_ThenSqlContainsTableAlias()
        {
            const string tableAlias = "qty";
            var expression = new BinaryExpression(BinaryOperator.Equal, FieldName.Quantity, null, 5.4m);
            var context = CreateContext(tableAlias);

            QuantityQueryGenerator.Instance.VisitBinary(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.Contains($"{tableAlias}.{VLatest.QuantitySearchParam.SingleValue.Metadata.Name}", sql);
        }

        [Theory]
        [InlineData("mg")]
        [InlineData("kg")]
        [InlineData("g")]
        [InlineData("mmol/L")]
        public void GivenVariousQuantityCodes_WhenVisited_ThenGeneratesSQL(string codeValue)
        {
            _model.TryGetQuantityCodeId(codeValue, out Arg.Any<int>())
                .Returns(x =>
                {
                    x[1] = 1;
                    return true;
                });

            var expression = new StringExpression(StringOperator.Equals, FieldName.QuantityCode, null, codeValue, true);
            var context = CreateContext();

            QuantityQueryGenerator.Instance.VisitString(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.NotEmpty(sql);
            Assert.Contains(VLatest.QuantitySearchParam.QuantityCodeId.Metadata.Name, sql);
        }

        [Fact]
        public void GivenInvalidFieldNameForBinary_WhenVisited_ThenThrowsArgumentOutOfRangeException()
        {
            var expression = new BinaryExpression(BinaryOperator.Equal, FieldName.TokenCode, null, 5.4m);
            var context = CreateContext();

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                QuantityQueryGenerator.Instance.VisitBinary(expression, context));
        }

        [Fact]
        public void GivenInvalidFieldNameForString_WhenVisited_ThenThrowsArgumentOutOfRangeException()
        {
            var expression = new StringExpression(StringOperator.Equals, FieldName.String, null, "invalid", true);
            var context = CreateContext();

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                QuantityQueryGenerator.Instance.VisitString(expression, context));
        }

        [Fact]
        public void GivenQuantityRangeQuery_WhenUsingLowAndHigh_ThenGeneratesCorrectSQL()
        {
            var lowExpression = new BinaryExpression(BinaryOperator.GreaterThanOrEqual, SqlFieldName.QuantityLow, null, 5.0m);
            var highExpression = new BinaryExpression(BinaryOperator.LessThanOrEqual, SqlFieldName.QuantityHigh, null, 10.0m);

            var contextLow = CreateContext();
            var contextHigh = CreateContext();

            QuantityQueryGenerator.Instance.VisitBinary(lowExpression, contextLow);
            QuantityQueryGenerator.Instance.VisitBinary(highExpression, contextHigh);

            var sqlLow = contextLow.StringBuilder.ToString();
            var sqlHigh = contextHigh.StringBuilder.ToString();

            Assert.Contains(VLatest.QuantitySearchParam.LowValue.Metadata.Name, sqlLow);
            Assert.Contains(">=", sqlLow);

            Assert.Contains(VLatest.QuantitySearchParam.HighValue.Metadata.Name, sqlHigh);
            Assert.Contains("<=", sqlHigh);
        }

        [Theory]
        [InlineData(0.001)]
        [InlineData(5.4)]
        [InlineData(100.0)]
        [InlineData(999.999)]
        public void GivenVariousQuantityValues_WhenVisited_ThenGeneratesSQL(decimal value)
        {
            var expression = new BinaryExpression(BinaryOperator.Equal, FieldName.Quantity, null, value);
            var context = CreateContext();

            QuantityQueryGenerator.Instance.VisitBinary(expression, context);

            var sql = context.StringBuilder.ToString();

            Assert.NotEmpty(sql);
            Assert.Contains(VLatest.QuantitySearchParam.SingleValue.Metadata.Name, sql);
            Assert.True(context.Parameters.HasParametersToHash);
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
