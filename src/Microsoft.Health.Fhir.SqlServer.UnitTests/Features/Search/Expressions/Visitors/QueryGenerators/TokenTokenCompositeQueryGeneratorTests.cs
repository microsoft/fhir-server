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
    /// Unit tests for TokenTokenCompositeQueryGenerator.
    /// Tests the generator's ability to delegate to Token generators for both components.
    /// Unique characteristic: Both components use the same generator type (TokenQueryGenerator).
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class TokenTokenCompositeQueryGeneratorTests
    {
        private readonly ISqlServerFhirModel _model;
        private readonly SchemaInformation _schemaInformation;

        public TokenTokenCompositeQueryGeneratorTests()
        {
            _model = Substitute.For<ISqlServerFhirModel>();
            _schemaInformation = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max);
            _schemaInformation.Current = SchemaVersionConstants.Max;
        }

        [Fact]
        public void GivenTokenTokenCompositeQueryGenerator_WhenTableAccessed_ThenReturnsTokenTokenCompositeSearchParamTable()
        {
            var table = TokenTokenCompositeQueryGenerator.Instance.Table;

            Assert.NotNull(table);
            Assert.Equal(VLatest.TokenTokenCompositeSearchParam.TableName, table.TableName);
        }

        [Fact]
        public void GivenStringExpressionForFirstTokenWithComponentIndex0_WhenVisitString_ThenDelegatesToTokenQueryGenerator()
        {
            // Arrange - Component index 0 should delegate to TokenQueryGenerator (first token)
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
            TokenTokenCompositeQueryGenerator.Instance.VisitString(expression, context);

            // Assert
            var sql = context.StringBuilder.ToString();
            Assert.NotEmpty(sql);

            // TokenQueryGenerator should generate SQL for token code with component index suffix
            Assert.Matches(@"Code1\s*=\s*@\w+", sql);
        }

        [Fact]
        public void GivenStringExpressionForSecondTokenWithComponentIndex1_WhenVisitString_ThenDelegatesToTokenQueryGenerator()
        {
            // Arrange - Component index 1 should delegate to TokenQueryGenerator (second token)
            _model.TryGetSystemId(Arg.Any<string>(), out Arg.Any<int>()).Returns(x =>
            {
                x[1] = 2;
                return true;
            });

            var expression = new StringExpression(
                StringOperator.Equals,
                FieldName.TokenCode,
                componentIndex: 1,
                value: "completed",
                ignoreCase: false);

            var context = CreateContext();

            // Act
            TokenTokenCompositeQueryGenerator.Instance.VisitString(expression, context);

            // Assert
            var sql = context.StringBuilder.ToString();
            Assert.NotEmpty(sql);

            // TokenQueryGenerator should generate SQL for token code on component 2
            Assert.Matches(@"Code2\s*=\s*@\w+", sql);
        }

        [Fact]
        public void GivenMissingFieldExpressionForFirstTokenWithComponentIndex0_WhenVisitMissingField_ThenDelegatesToTokenQueryGenerator()
        {
            // Arrange
            var expression = new MissingFieldExpression(FieldName.TokenSystem, componentIndex: 0);
            var context = CreateContext();

            // Act
            TokenTokenCompositeQueryGenerator.Instance.VisitMissingField(expression, context);

            // Assert
            var sql = context.StringBuilder.ToString();
            Assert.NotEmpty(sql);

            // Should check for NULL token system
            Assert.Contains("IS NULL", sql);
        }

        [Fact]
        public void GivenBothTokenComponentExpressions_WhenVisited_ThenBothGenerateSql()
        {
            // Arrange - Test Token1 (index 0) + Token2 (index 1) combination
            _model.TryGetSystemId(Arg.Any<string>(), out Arg.Any<int>()).Returns(x =>
            {
                x[1] = 1;
                return true;
            });

            var token1Expression = new StringExpression(
                StringOperator.Equals,
                FieldName.TokenCode,
                componentIndex: 0,
                value: "active",
                ignoreCase: false);

            var token2Expression = new StringExpression(
                StringOperator.Equals,
                FieldName.TokenCode,
                componentIndex: 1,
                value: "completed",
                ignoreCase: false);

            var context = CreateContext();

            // Act
            TokenTokenCompositeQueryGenerator.Instance.VisitString(token1Expression, context);
            var sqlAfterToken1 = context.StringBuilder.ToString();

            TokenTokenCompositeQueryGenerator.Instance.VisitString(token2Expression, context);
            var sqlAfterBoth = context.StringBuilder.ToString();

            // Assert
            Assert.NotEmpty(sqlAfterToken1);
            Assert.NotEmpty(sqlAfterBoth);
            Assert.True(sqlAfterBoth.Length > sqlAfterToken1.Length, "Both components should generate SQL");

            // Should contain both Code1 and Code2 with proper predicate structure for the two token components
            Assert.Matches(@"Code1\s*=\s*@\w+", sqlAfterBoth);
            Assert.Matches(@"Code2\s*=\s*@\w+", sqlAfterBoth);
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
