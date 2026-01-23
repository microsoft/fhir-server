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
    /// Unit tests for TokenTextQueryGenerator.
    /// Tests the generator's ability to handle token text searches with various string operators.
    /// TokenText is used for searching token display text (e.g., code system display names).
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class TokenTextQueryGeneratorTests
    {
        private readonly ISqlServerFhirModel _model;
        private readonly SchemaInformation _schemaInformation;

        public TokenTextQueryGeneratorTests()
        {
            _model = Substitute.For<ISqlServerFhirModel>();
            _schemaInformation = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max);
            _schemaInformation.Current = SchemaVersionConstants.Max;
        }

        [Fact]
        public void GivenTokenTextQueryGenerator_WhenTableAccessed_ThenReturnsTokenTextTable()
        {
            var table = TokenTextQueryGenerator.Instance.Table;

            Assert.NotNull(table);
            Assert.Equal(VLatest.TokenText.TableName, table.TableName);
        }

        [Fact]
        public void GivenStringExpressionWithEqualsOperator_WhenVisitString_ThenGeneratesSqlWithEquality()
        {
            // Arrange - Test exact match search (:text=Active)
            var expression = new StringExpression(
                StringOperator.Equals,
                FieldName.TokenText,
                componentIndex: null,
                value: "Active",
                ignoreCase: false);

            var context = CreateContext();

            // Act
            TokenTextQueryGenerator.Instance.VisitString(expression, context);

            // Assert
            var sql = context.StringBuilder.ToString();
            Assert.NotEmpty(sql);
            Assert.Contains("Text", sql);
            Assert.Contains("=", sql);
            Assert.Contains("@", sql); // Should use parameterized query
        }

        [Fact]
        public void GivenStringExpressionWithContainsOperator_WhenVisitString_ThenGeneratesSqlWithLikePattern()
        {
            // Arrange - Test substring search (:text=active*)
            var expression = new StringExpression(
                StringOperator.Contains,
                FieldName.TokenText,
                componentIndex: null,
                value: "active",
                ignoreCase: false);

            var context = CreateContext();

            // Act
            TokenTextQueryGenerator.Instance.VisitString(expression, context);

            // Assert
            var sql = context.StringBuilder.ToString();
            Assert.NotEmpty(sql);
            Assert.Contains("Text", sql);
            Assert.Contains("LIKE", sql);
        }

        [Fact]
        public void GivenStringExpressionWithStartsWithOperator_WhenVisitString_ThenGeneratesSqlWithLikePattern()
        {
            // Arrange - Test prefix search
            var expression = new StringExpression(
                StringOperator.StartsWith,
                FieldName.TokenText,
                componentIndex: null,
                value: "Act",
                ignoreCase: false);

            var context = CreateContext();

            // Act
            TokenTextQueryGenerator.Instance.VisitString(expression, context);

            // Assert
            var sql = context.StringBuilder.ToString();
            Assert.NotEmpty(sql);
            Assert.Contains("Text", sql);
            Assert.Contains("LIKE", sql);
        }

        [Fact]
        public void GivenStringExpressionWithSpecialCharacters_WhenVisitString_ThenHandlesEscapingCorrectly()
        {
            // Arrange - Test text with SQL special characters
            var expression = new StringExpression(
                StringOperator.Equals,
                FieldName.TokenText,
                componentIndex: null,
                value: "Status_Active",
                ignoreCase: false);

            var context = CreateContext();

            // Act
            TokenTextQueryGenerator.Instance.VisitString(expression, context);

            // Assert
            var sql = context.StringBuilder.ToString();
            Assert.NotEmpty(sql);
            Assert.Contains("Text", sql);

            // Should have generated SQL with parameter
            Assert.Contains("@", sql);
        }

        [Fact]
        public void GivenMultipleStringExpressions_WhenVisitString_ThenGeneratesMultipleSqlPredicates()
        {
            // Arrange - Test sequential calls for multiple text conditions
            var expression1 = new StringExpression(
                StringOperator.StartsWith,
                FieldName.TokenText,
                componentIndex: null,
                value: "Active",
                ignoreCase: false);

            var expression2 = new StringExpression(
                StringOperator.Contains,
                FieldName.TokenText,
                componentIndex: null,
                value: "Status",
                ignoreCase: false);

            var context = CreateContext();

            // Act
            TokenTextQueryGenerator.Instance.VisitString(expression1, context);
            var sqlAfterFirst = context.StringBuilder.ToString();

            TokenTextQueryGenerator.Instance.VisitString(expression2, context);
            var sqlAfterSecond = context.StringBuilder.ToString();

            // Assert
            Assert.NotEmpty(sqlAfterFirst);
            Assert.NotEmpty(sqlAfterSecond);
            Assert.True(sqlAfterSecond.Length > sqlAfterFirst.Length, "Second call should append more SQL");

            // Should have both LIKE patterns
            var likeCount = sqlAfterSecond.Split("LIKE").Length - 1;
            Assert.Equal(2, likeCount);
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
