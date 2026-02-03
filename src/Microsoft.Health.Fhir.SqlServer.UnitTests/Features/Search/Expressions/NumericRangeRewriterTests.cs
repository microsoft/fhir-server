// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions
{
    /// <summary>
    /// Unit tests for NumericRangeRewriter.
    /// Tests the rewriter's ability to transform numeric and quantity expressions
    /// to account for range values (low/high fields).
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class NumericRangeRewriterTests
    {
        [Fact]
        public void GivenQuantityGreaterThanExpression_WhenRewritten_ThenUsesQuantityHighField()
        {
            // Arrange
            var expression = Expression.GreaterThan(FieldName.Quantity, null, 5.0m);
            var sqlRoot = CreateSqlRootWithExpression(expression);

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(NumericRangeRewriter.Instance, null);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.SearchParamTableExpressions.Count);

            // First expression is the original
            var originalExpr = (BinaryExpression)result.SearchParamTableExpressions[0].Predicate;
            Assert.Equal(FieldName.Quantity, originalExpr.FieldName);
            Assert.Equal(BinaryOperator.GreaterThan, originalExpr.BinaryOperator);

            // Second expression is the concatenation using QuantityHigh
            var concatenationExpr = (BinaryExpression)result.SearchParamTableExpressions[1].Predicate;
            Assert.Equal(SqlFieldName.QuantityHigh, concatenationExpr.FieldName);
            Assert.Equal(BinaryOperator.GreaterThan, concatenationExpr.BinaryOperator);
            Assert.Equal(5.0m, concatenationExpr.Value);
            Assert.Equal(SearchParamTableExpressionKind.Concatenation, result.SearchParamTableExpressions[1].Kind);
        }

        [Fact]
        public void GivenQuantityGreaterThanOrEqualExpression_WhenRewritten_ThenUsesQuantityHighField()
        {
            // Arrange
            var expression = Expression.GreaterThanOrEqual(FieldName.Quantity, null, 10.5m);
            var sqlRoot = CreateSqlRootWithExpression(expression);

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(NumericRangeRewriter.Instance, null);

            // Assert
            Assert.Equal(2, result.SearchParamTableExpressions.Count);

            var concatenationExpr = (BinaryExpression)result.SearchParamTableExpressions[1].Predicate;
            Assert.Equal(SqlFieldName.QuantityHigh, concatenationExpr.FieldName);
            Assert.Equal(BinaryOperator.GreaterThanOrEqual, concatenationExpr.BinaryOperator);
            Assert.Equal(10.5m, concatenationExpr.Value);
        }

        [Fact]
        public void GivenQuantityLessThanExpression_WhenRewritten_ThenUsesQuantityLowField()
        {
            // Arrange
            var expression = Expression.LessThan(FieldName.Quantity, null, 100.0m);
            var sqlRoot = CreateSqlRootWithExpression(expression);

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(NumericRangeRewriter.Instance, null);

            // Assert
            Assert.Equal(2, result.SearchParamTableExpressions.Count);

            var concatenationExpr = (BinaryExpression)result.SearchParamTableExpressions[1].Predicate;
            Assert.Equal(SqlFieldName.QuantityLow, concatenationExpr.FieldName);
            Assert.Equal(BinaryOperator.LessThan, concatenationExpr.BinaryOperator);
            Assert.Equal(100.0m, concatenationExpr.Value);
        }

        [Fact]
        public void GivenQuantityLessThanOrEqualExpression_WhenRewritten_ThenUsesQuantityLowField()
        {
            // Arrange
            var expression = Expression.LessThanOrEqual(FieldName.Quantity, null, 50.25m);
            var sqlRoot = CreateSqlRootWithExpression(expression);

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(NumericRangeRewriter.Instance, null);

            // Assert
            Assert.Equal(2, result.SearchParamTableExpressions.Count);

            var concatenationExpr = (BinaryExpression)result.SearchParamTableExpressions[1].Predicate;
            Assert.Equal(SqlFieldName.QuantityLow, concatenationExpr.FieldName);
            Assert.Equal(BinaryOperator.LessThanOrEqual, concatenationExpr.BinaryOperator);
            Assert.Equal(50.25m, concatenationExpr.Value);
        }

        [Fact]
        public void GivenNumberGreaterThanExpression_WhenRewritten_ThenUsesNumberHighField()
        {
            // Arrange
            var expression = Expression.GreaterThan(FieldName.Number, null, 42);
            var sqlRoot = CreateSqlRootWithExpression(expression);

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(NumericRangeRewriter.Instance, null);

            // Assert
            Assert.Equal(2, result.SearchParamTableExpressions.Count);

            var concatenationExpr = (BinaryExpression)result.SearchParamTableExpressions[1].Predicate;
            Assert.Equal(SqlFieldName.NumberHigh, concatenationExpr.FieldName);
            Assert.Equal(BinaryOperator.GreaterThan, concatenationExpr.BinaryOperator);
            Assert.Equal(42, concatenationExpr.Value);
        }

        [Fact]
        public void GivenNumberLessThanExpression_WhenRewritten_ThenUsesNumberLowField()
        {
            // Arrange
            var expression = Expression.LessThan(FieldName.Number, null, 99);
            var sqlRoot = CreateSqlRootWithExpression(expression);

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(NumericRangeRewriter.Instance, null);

            // Assert
            Assert.Equal(2, result.SearchParamTableExpressions.Count);

            var concatenationExpr = (BinaryExpression)result.SearchParamTableExpressions[1].Predicate;
            Assert.Equal(SqlFieldName.NumberLow, concatenationExpr.FieldName);
            Assert.Equal(BinaryOperator.LessThan, concatenationExpr.BinaryOperator);
        }

        [Fact]
        public void GivenNumberGreaterThanOrEqualExpression_WhenRewritten_ThenUsesNumberHighField()
        {
            // Arrange
            var expression = Expression.GreaterThanOrEqual(FieldName.Number, null, 0);
            var sqlRoot = CreateSqlRootWithExpression(expression);

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(NumericRangeRewriter.Instance, null);

            // Assert
            Assert.Equal(2, result.SearchParamTableExpressions.Count);

            var concatenationExpr = (BinaryExpression)result.SearchParamTableExpressions[1].Predicate;
            Assert.Equal(SqlFieldName.NumberHigh, concatenationExpr.FieldName);
            Assert.Equal(BinaryOperator.GreaterThanOrEqual, concatenationExpr.BinaryOperator);
        }

        [Fact]
        public void GivenNumberLessThanOrEqualExpression_WhenRewritten_ThenUsesNumberLowField()
        {
            // Arrange
            var expression = Expression.LessThanOrEqual(FieldName.Number, null, 1000);
            var sqlRoot = CreateSqlRootWithExpression(expression);

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(NumericRangeRewriter.Instance, null);

            // Assert
            Assert.Equal(2, result.SearchParamTableExpressions.Count);

            var concatenationExpr = (BinaryExpression)result.SearchParamTableExpressions[1].Predicate;
            Assert.Equal(SqlFieldName.NumberLow, concatenationExpr.FieldName);
            Assert.Equal(BinaryOperator.LessThanOrEqual, concatenationExpr.BinaryOperator);
        }

        [Fact]
        public void GivenNonNumericExpression_WhenRewritten_ThenNoRewriteOccurs()
        {
            // Arrange - String expression should not be rewritten
            var expression = Expression.Equals(FieldName.String, null, "test");
            var sqlRoot = CreateSqlRootWithExpression(expression);

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(NumericRangeRewriter.Instance, null);

            // Assert - Should return same expression
            Assert.Same(sqlRoot, result);
            Assert.Single(result.SearchParamTableExpressions);
        }

        [Fact]
        public void GivenTokenCodeExpression_WhenRewritten_ThenNoRewriteOccurs()
        {
            // Arrange
            var expression = Expression.Equals(FieldName.TokenCode, null, "code");
            var sqlRoot = CreateSqlRootWithExpression(expression);

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(NumericRangeRewriter.Instance, null);

            // Assert
            Assert.Same(sqlRoot, result);
            Assert.Single(result.SearchParamTableExpressions);
        }

        [Fact]
        public void GivenQuantityWithComponentIndex_WhenRewritten_ThenComponentIndexPreserved()
        {
            // Arrange
            var componentIndex = 1;
            var expression = Expression.GreaterThan(FieldName.Quantity, componentIndex, 5.0m);
            var sqlRoot = CreateSqlRootWithExpression(expression);

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(NumericRangeRewriter.Instance, null);

            // Assert
            Assert.Equal(2, result.SearchParamTableExpressions.Count);

            var concatenationExpr = (BinaryExpression)result.SearchParamTableExpressions[1].Predicate;
            Assert.Equal(SqlFieldName.QuantityHigh, concatenationExpr.FieldName);
            Assert.Equal(componentIndex, concatenationExpr.ComponentIndex);
        }

        [Fact]
        public void GivenNumberWithComponentIndex_WhenRewritten_ThenComponentIndexPreserved()
        {
            // Arrange
            var componentIndex = 2;
            var expression = Expression.LessThan(FieldName.Number, componentIndex, 100);
            var sqlRoot = CreateSqlRootWithExpression(expression);

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(NumericRangeRewriter.Instance, null);

            // Assert
            Assert.Equal(2, result.SearchParamTableExpressions.Count);

            var concatenationExpr = (BinaryExpression)result.SearchParamTableExpressions[1].Predicate;
            Assert.Equal(SqlFieldName.NumberLow, concatenationExpr.FieldName);
            Assert.Equal(componentIndex, concatenationExpr.ComponentIndex);
        }

        [Fact]
        public void GivenMultipleNumericExpressions_WhenRewritten_ThenAllAreRewritten()
        {
            // Arrange
            var expr1 = Expression.GreaterThan(FieldName.Number, null, 10);
            var expr2 = Expression.LessThan(FieldName.Quantity, null, 50.0m);

            var tableExpressions = new System.Collections.Generic.List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, expr1, SearchParamTableExpressionKind.Normal),
                new SearchParamTableExpression(null, expr2, SearchParamTableExpressionKind.Normal),
            };

            var sqlRoot = new SqlRootExpression(tableExpressions, new System.Collections.Generic.List<SearchParameterExpressionBase>());

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(NumericRangeRewriter.Instance, null);

            // Assert - Each expression should be doubled (original + concatenation)
            Assert.Equal(4, result.SearchParamTableExpressions.Count);

            // First original + concatenation
            Assert.Equal(SearchParamTableExpressionKind.Normal, result.SearchParamTableExpressions[0].Kind);
            Assert.Equal(SearchParamTableExpressionKind.Concatenation, result.SearchParamTableExpressions[1].Kind);

            // Second original + concatenation
            Assert.Equal(SearchParamTableExpressionKind.Normal, result.SearchParamTableExpressions[2].Kind);
            Assert.Equal(SearchParamTableExpressionKind.Concatenation, result.SearchParamTableExpressions[3].Kind);
        }

        [Fact]
        public void GivenMixedNumericAndNonNumericExpressions_WhenRewritten_ThenOnlyNumericRewritten()
        {
            // Arrange
            var numericExpr = Expression.GreaterThan(FieldName.Number, null, 5);
            var stringExpr = Expression.Equals(FieldName.String, null, "test");

            var tableExpressions = new System.Collections.Generic.List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, numericExpr, SearchParamTableExpressionKind.Normal),
                new SearchParamTableExpression(null, stringExpr, SearchParamTableExpressionKind.Normal),
            };

            var sqlRoot = new SqlRootExpression(tableExpressions, new System.Collections.Generic.List<SearchParameterExpressionBase>());

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(NumericRangeRewriter.Instance, null);

            // Assert - Numeric gets concatenation, string does not
            Assert.Equal(3, result.SearchParamTableExpressions.Count);

            Assert.Equal(SearchParamTableExpressionKind.Normal, result.SearchParamTableExpressions[0].Kind);
            Assert.Equal(SearchParamTableExpressionKind.Concatenation, result.SearchParamTableExpressions[1].Kind);
            Assert.Equal(SearchParamTableExpressionKind.Normal, result.SearchParamTableExpressions[2].Kind);
            Assert.Same(stringExpr, result.SearchParamTableExpressions[2].Predicate);
        }

        [Fact]
        public void GivenEmptySqlRoot_WhenRewritten_ThenReturnsUnchanged()
        {
            // Arrange
            var sqlRoot = new SqlRootExpression(
                new System.Collections.Generic.List<SearchParamTableExpression>(),
                new System.Collections.Generic.List<SearchParameterExpressionBase>());

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(NumericRangeRewriter.Instance, null);

            // Assert
            Assert.Same(sqlRoot, result);
        }

        [Fact]
        public void GivenChainExpression_WhenRewritten_ThenSkipsChainExpressions()
        {
            // Arrange - Chain expressions should be skipped by ConcatenationRewriter
            var expression = Expression.GreaterThan(FieldName.Number, null, 10);
            var tableExpressions = new System.Collections.Generic.List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, expression, SearchParamTableExpressionKind.Chain),
            };

            var sqlRoot = new SqlRootExpression(tableExpressions, new System.Collections.Generic.List<SearchParameterExpressionBase>());

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(NumericRangeRewriter.Instance, null);

            // Assert - Chain expression should not be rewritten
            Assert.Same(sqlRoot, result);
            Assert.Single(result.SearchParamTableExpressions);
        }

        [Fact]
        public void GivenIncludeExpression_WhenRewritten_ThenSkipsIncludeExpressions()
        {
            // Arrange
            var expression = Expression.GreaterThan(FieldName.Number, null, 10);
            var tableExpressions = new System.Collections.Generic.List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, expression, SearchParamTableExpressionKind.Include),
            };

            var sqlRoot = new SqlRootExpression(tableExpressions, new System.Collections.Generic.List<SearchParameterExpressionBase>());

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(NumericRangeRewriter.Instance, null);

            // Assert
            Assert.Same(sqlRoot, result);
            Assert.Single(result.SearchParamTableExpressions);
        }

        [Fact]
        public void GivenSortExpression_WhenRewritten_ThenSkipsSortExpressions()
        {
            // Arrange
            var expression = Expression.GreaterThan(FieldName.Number, null, 10);
            var tableExpressions = new System.Collections.Generic.List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, expression, SearchParamTableExpressionKind.Sort),
            };

            var sqlRoot = new SqlRootExpression(tableExpressions, new System.Collections.Generic.List<SearchParameterExpressionBase>());

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(NumericRangeRewriter.Instance, null);

            // Assert
            Assert.Same(sqlRoot, result);
            Assert.Single(result.SearchParamTableExpressions);
        }

        [Fact]
        public void GivenAllExpression_WhenRewritten_ThenSkipsAllExpressions()
        {
            // Arrange
            var expression = Expression.GreaterThan(FieldName.Number, null, 10);
            var tableExpressions = new System.Collections.Generic.List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, expression, SearchParamTableExpressionKind.All),
            };

            var sqlRoot = new SqlRootExpression(tableExpressions, new System.Collections.Generic.List<SearchParameterExpressionBase>());

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(NumericRangeRewriter.Instance, null);

            // Assert
            Assert.Same(sqlRoot, result);
            Assert.Single(result.SearchParamTableExpressions);
        }

        private static SqlRootExpression CreateSqlRootWithExpression(Expression expression)
        {
            var tableExpressions = new System.Collections.Generic.List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, expression, SearchParamTableExpressionKind.Normal),
            };

            return new SqlRootExpression(tableExpressions, new System.Collections.Generic.List<SearchParameterExpressionBase>());
        }
    }
}
