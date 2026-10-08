// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
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
    /// Unit tests for TokenNumberNumberQueryGenerator.
    /// Tests the generator's ability to delegate to three component generators (Token + Number + Number).
    /// This is the only composite generator with three components.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class TokenNumberNumberQueryGeneratorTests
    {
        private readonly ISqlServerFhirModel _model;
        private readonly SchemaInformation _schemaInformation;

        public TokenNumberNumberQueryGeneratorTests()
        {
            _model = Substitute.For<ISqlServerFhirModel>();
            _schemaInformation = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max);
            _schemaInformation.Current = SchemaVersionConstants.Max;
        }

        [Fact]
        public void GivenTokenNumberNumberQueryGenerator_WhenTableAccessed_ThenReturnsTokenNumberNumberCompositeSearchParamTable()
        {
            var table = TokenNumberNumberQueryGenerator.Instance.Table;

            Assert.NotNull(table);
            Assert.Equal(VLatest.TokenNumberNumberCompositeSearchParam.TableName, table.TableName);
        }

        [Fact]
        public void GivenStringExpressionForTokenWithComponentIndex0_WhenVisitString_ThenDelegatesToTokenQueryGenerator()
        {
            // Arrange - Component index 0 should delegate to TokenQueryGenerator
            _model.TryGetSystemId(Arg.Any<string>(), out Arg.Any<int>()).Returns(x =>
            {
                x[1] = 1;
                return true;
            });

            var expression = new StringExpression(
                StringOperator.Equals,
                FieldName.TokenCode,
                componentIndex: 0,
                value: "mg",
                ignoreCase: false);

            var context = CreateContext();

            // Act
            TokenNumberNumberQueryGenerator.Instance.VisitString(expression, context);

            // Assert
            var sql = context.StringBuilder.ToString();
            Assert.NotEmpty(sql);

            // TokenQueryGenerator should generate SQL for token code on component 1
            Assert.Matches(@"Code1\s*=\s*@\w+", sql);
        }

        [Fact]
        public void GivenBinaryExpressionForFirstNumberWithComponentIndex1_WhenVisitBinary_ThenDelegatesToNumberQueryGenerator()
        {
            // Arrange - Component index 1 should delegate to NumberQueryGenerator (first number)
            var expression = new BinaryExpression(
                BinaryOperator.GreaterThanOrEqual,
                FieldName.Number,
                componentIndex: 1,
                value: 10.5m);

            var context = CreateContext();

            // Act
            TokenNumberNumberQueryGenerator.Instance.VisitBinary(expression, context);

            // Assert
            var sql = context.StringBuilder.ToString();
            Assert.NotEmpty(sql);

            // NumberQueryGenerator should generate SQL for number comparison with null-guard
            Assert.Contains("SingleValue2 IS NOT NULL", sql);
            Assert.Matches(@"SingleValue2\s*>=\s*@\w+", sql);
        }

        [Fact]
        public void GivenBinaryExpressionForSecondNumberWithComponentIndex2_WhenVisitBinary_ThenDelegatesToNumberQueryGenerator()
        {
            // Arrange - Component index 2 should delegate to NumberQueryGenerator (second number)
            var expression = new BinaryExpression(
                BinaryOperator.LessThan,
                FieldName.Number,
                componentIndex: 2,
                value: 100.0m);

            var context = CreateContext();

            // Act
            TokenNumberNumberQueryGenerator.Instance.VisitBinary(expression, context);

            // Assert
            var sql = context.StringBuilder.ToString();
            Assert.NotEmpty(sql);

            // NumberQueryGenerator should generate SQL for number comparison with null-guard
            Assert.Contains("SingleValue3 IS NOT NULL", sql);
            Assert.Matches(@"SingleValue3\s*<\s*@\w+", sql);
        }

        [Fact]
        public void GivenMissingFieldExpressionForTokenWithComponentIndex0_WhenVisitMissingField_ThenDelegatesToTokenQueryGenerator()
        {
            // Arrange
            var expression = new MissingFieldExpression(FieldName.TokenSystem, componentIndex: 0);
            var context = CreateContext();

            // Act
            TokenNumberNumberQueryGenerator.Instance.VisitMissingField(expression, context);

            // Assert
            var sql = context.StringBuilder.ToString();
            Assert.NotEmpty(sql);

            // Should check for NULL token system
            Assert.Contains("IS NULL", sql);
        }

        [Fact]
        public void GivenAllThreeComponentExpressions_WhenVisited_ThenAllGenerateSql()
        {
            // Arrange - Test Token (index 0) + Number1 (index 1) + Number2 (index 2) combination
            _model.TryGetSystemId(Arg.Any<string>(), out Arg.Any<int>()).Returns(x =>
            {
                x[1] = 1;
                return true;
            });

            var tokenExpression = new StringExpression(
                StringOperator.Equals,
                FieldName.TokenCode,
                componentIndex: 0,
                value: "mg",
                ignoreCase: false);

            var number1Expression = new BinaryExpression(
                BinaryOperator.GreaterThanOrEqual,
                FieldName.Number,
                componentIndex: 1,
                value: 10.0m);

            var number2Expression = new BinaryExpression(
                BinaryOperator.LessThan,
                FieldName.Number,
                componentIndex: 2,
                value: 100.0m);

            var context = CreateContext();

            // Act - Visit all three components
            TokenNumberNumberQueryGenerator.Instance.VisitString(tokenExpression, context);
            var sqlAfterToken = context.StringBuilder.ToString();

            TokenNumberNumberQueryGenerator.Instance.VisitBinary(number1Expression, context);
            var sqlAfterNumber1 = context.StringBuilder.ToString();

            TokenNumberNumberQueryGenerator.Instance.VisitBinary(number2Expression, context);
            var sqlAfterAll = context.StringBuilder.ToString();

            // Assert
            Assert.NotEmpty(sqlAfterToken);
            Assert.NotEmpty(sqlAfterNumber1);
            Assert.NotEmpty(sqlAfterAll);

            Assert.True(sqlAfterNumber1.Length > sqlAfterToken.Length, "Second component should add SQL");
            Assert.True(sqlAfterAll.Length > sqlAfterNumber1.Length, "Third component should add SQL");

            // Should contain all three component SQL elements
            Assert.Contains("Code", sqlAfterAll);

            // Should have both number comparisons
            Assert.Contains(">=", sqlAfterAll);
            Assert.Contains("<", sqlAfterAll);
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
    }
}
