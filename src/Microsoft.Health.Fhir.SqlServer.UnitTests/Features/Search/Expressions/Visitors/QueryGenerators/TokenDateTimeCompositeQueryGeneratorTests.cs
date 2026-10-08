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
    /// Unit tests for TokenDateTimeCompositeQueryGenerator.
    /// Tests the generator's ability to delegate to Token and DateTime component generators.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class TokenDateTimeCompositeQueryGeneratorTests
    {
        private readonly ISqlServerFhirModel _model;
        private readonly SchemaInformation _schemaInformation;

        public TokenDateTimeCompositeQueryGeneratorTests()
        {
            _model = Substitute.For<ISqlServerFhirModel>();
            _schemaInformation = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max);
            _schemaInformation.Current = SchemaVersionConstants.Max;
        }

        [Fact]
        public void GivenTokenDateTimeCompositeQueryGenerator_WhenTableAccessed_ThenReturnsTokenDateTimeCompositeSearchParamTable()
        {
            var table = TokenDateTimeCompositeQueryGenerator.Instance.Table;

            Assert.NotNull(table);
            Assert.Equal(VLatest.TokenDateTimeCompositeSearchParam.TableName, table.TableName);
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
                value: "active",
                ignoreCase: false);

            var context = CreateContext();

            // Act
            TokenDateTimeCompositeQueryGenerator.Instance.VisitString(expression, context);

            // Assert
            var sql = context.StringBuilder.ToString();
            Assert.NotEmpty(sql);

            // TokenQueryGenerator should generate SQL for token code on component 1
            Assert.Matches(@"Code1\s*=\s*@\w+", sql);
        }

        [Fact]
        public void GivenBinaryExpressionForDateTimeWithComponentIndex1_WhenVisitBinary_ThenDelegatesToDateTimeQueryGenerator()
        {
            // Arrange - Component index 1 should delegate to DateTimeQueryGenerator
            var dateValue = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);
            var expression = new BinaryExpression(
                BinaryOperator.GreaterThanOrEqual,
                FieldName.DateTimeStart,
                componentIndex: 1,
                value: dateValue);

            var context = CreateContext();

            // Act
            TokenDateTimeCompositeQueryGenerator.Instance.VisitBinary(expression, context);

            // Assert
            var sql = context.StringBuilder.ToString();
            Assert.NotEmpty(sql);

            // DateTimeQueryGenerator should generate SQL for datetime comparison on component 2
            Assert.Matches(@"StartDateTime2\s*>=\s*@\w+", sql);
        }

        [Fact]
        public void GivenMissingFieldExpressionForTokenWithComponentIndex0_WhenVisitMissingField_ThenDelegatesToTokenQueryGenerator()
        {
            // Arrange
            var expression = new MissingFieldExpression(FieldName.TokenSystem, componentIndex: 0);
            var context = CreateContext();

            // Act
            TokenDateTimeCompositeQueryGenerator.Instance.VisitMissingField(expression, context);

            // Assert
            var sql = context.StringBuilder.ToString();
            Assert.NotEmpty(sql);

            // Should check for NULL token system
            Assert.Contains("IS NULL", sql);
        }

        [Fact]
        public void GivenBothComponentExpressions_WhenVisited_ThenBothGenerateSql()
        {
            // Arrange - Test Token (index 0) + DateTime (index 1) combination
            _model.TryGetSystemId(Arg.Any<string>(), out Arg.Any<int>()).Returns(x =>
            {
                x[1] = 1;
                return true;
            });

            var tokenExpression = new StringExpression(
                StringOperator.Equals,
                FieldName.TokenCode,
                componentIndex: 0,
                value: "completed",
                ignoreCase: false);

            var dateValue = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var dateTimeExpression = new BinaryExpression(
                BinaryOperator.GreaterThan,
                FieldName.DateTimeStart,
                componentIndex: 1,
                value: dateValue);

            var context = CreateContext();

            // Act
            TokenDateTimeCompositeQueryGenerator.Instance.VisitString(tokenExpression, context);
            var sqlAfterToken = context.StringBuilder.ToString();

            TokenDateTimeCompositeQueryGenerator.Instance.VisitBinary(dateTimeExpression, context);
            var sqlAfterBoth = context.StringBuilder.ToString();

            // Assert
            Assert.NotEmpty(sqlAfterToken);
            Assert.NotEmpty(sqlAfterBoth);
            Assert.True(sqlAfterBoth.Length > sqlAfterToken.Length, "Both components should generate SQL");

            // Should contain both token and datetime SQL elements
            Assert.Contains("Code", sqlAfterBoth);
            Assert.Contains("StartDateTime", sqlAfterBoth);
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
