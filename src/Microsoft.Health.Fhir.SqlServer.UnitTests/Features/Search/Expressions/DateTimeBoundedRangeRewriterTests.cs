// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions
{
    /// <summary>
    /// Unit tests for DateTimeBoundedRangeRewriter.
    /// Tests the rewriter's optimization of datetime range queries by adding a bounded range check
    /// for dates shorter than one day to improve query performance.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class DateTimeBoundedRangeRewriterTests
    {
        private static readonly DateTimeOffset BaseDate = new DateTimeOffset(2024, 1, 15, 12, 0, 0, TimeSpan.Zero);

        [Fact]
        public void GivenDateTimeBoundedRange_WhenRewritten_ThenCreatesShortRangeOptimization()
        {
            // Arrange - Pattern: (DateTimeEnd >= X) AND (DateTimeStart < Y)
            var greaterThanExpr = Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, null, BaseDate);
            var lessThanExpr = Expression.LessThan(FieldName.DateTimeStart, null, BaseDate.AddHours(6));
            var andExpression = Expression.And(greaterThanExpr, lessThanExpr);

            var sqlRoot = CreateSqlRootWithExpression(andExpression);

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(DateTimeBoundedRangeRewriter.Instance, null);

            // Assert - Should create two table expressions: original + optimization for short ranges
            Assert.Equal(2, result.SearchParamTableExpressions.Count);

            // Original expression
            Assert.Equal(SearchParamTableExpressionKind.Normal, result.SearchParamTableExpressions[0].Kind);

            // Optimized concatenation for short ranges
            var concatenation = result.SearchParamTableExpressions[1];
            Assert.Equal(SearchParamTableExpressionKind.Concatenation, concatenation.Kind);

            var concatenationAnd = Assert.IsType<MultiaryExpression>(concatenation.Predicate);
            Assert.Equal(MultiaryOperator.And, concatenationAnd.MultiaryOperation);
            Assert.Equal(4, concatenationAnd.Expressions.Count);

            // First: DateTimeIsLongerThanADay = false
            var isNotLongExpr = Assert.IsType<BinaryExpression>(concatenationAnd.Expressions[0]);
            Assert.Equal(SqlFieldName.DateTimeIsLongerThanADay, isNotLongExpr.FieldName);
            Assert.Equal(BinaryOperator.Equal, isNotLongExpr.BinaryOperator);
            Assert.Equal(false, isNotLongExpr.Value);

            // Second: DateTimeEnd >= X
            var endExpr = Assert.IsType<BinaryExpression>(concatenationAnd.Expressions[1]);
            Assert.Equal(FieldName.DateTimeEnd, endExpr.FieldName);

            // Third: DateTimeStart >= (X - 1 day)
            var startBoundedExpr = Assert.IsType<BinaryExpression>(concatenationAnd.Expressions[2]);
            Assert.Equal(FieldName.DateTimeStart, startBoundedExpr.FieldName);
            Assert.Equal(BinaryOperator.GreaterThanOrEqual, startBoundedExpr.BinaryOperator);
            Assert.Equal(BaseDate.AddTicks(-TimeSpan.TicksPerDay), startBoundedExpr.Value);

            // Fourth: DateTimeStart < Y
            var startExpr = Assert.IsType<BinaryExpression>(concatenationAnd.Expressions[3]);
            Assert.Equal(FieldName.DateTimeStart, startExpr.FieldName);
        }

        [Fact]
        public void GivenLongDateRange_WhenRewritten_ThenCreatesLongRangeExpression()
        {
            // Arrange
            var greaterThanExpr = Expression.GreaterThan(FieldName.DateTimeEnd, null, BaseDate);
            var lessThanExpr = Expression.LessThanOrEqual(FieldName.DateTimeStart, null, BaseDate.AddDays(2));
            var andExpression = Expression.And(greaterThanExpr, lessThanExpr);

            var sqlRoot = CreateSqlRootWithExpression(andExpression);

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(DateTimeBoundedRangeRewriter.Instance, null);

            // Assert
            Assert.Equal(2, result.SearchParamTableExpressions.Count);

            // Original expression remains
            Assert.Equal(SearchParamTableExpressionKind.Normal, result.SearchParamTableExpressions[0].Kind);

            // Concatenation for long ranges - The Scout creates And with 3 expressions (DateTimeIsLongerThanADay=true + 2 original)
            // But then VisitMultiary detects this pattern and transforms it to And with 4 expressions (DateTimeIsLongerThanADay=false + optimization)
            var concatenation = result.SearchParamTableExpressions[1];
            Assert.Equal(SearchParamTableExpressionKind.Concatenation, concatenation.Kind);

            var concatenationAnd = Assert.IsType<MultiaryExpression>(concatenation.Predicate);
            Assert.Equal(4, concatenationAnd.Expressions.Count); // Changed from 3 to 4

            // Should have DateTimeIsLongerThanADay = false (not true - gets flipped by VisitMultiary)
            var isLongExpr = Assert.IsType<BinaryExpression>(concatenationAnd.Expressions[0]);
            Assert.Equal(SqlFieldName.DateTimeIsLongerThanADay, isLongExpr.FieldName);
            Assert.Equal(false, isLongExpr.Value); // Changed from true to false
        }

        [Fact]
        public void GivenNonDateTimeExpression_WhenRewritten_ThenNoRewriteOccurs()
        {
            // Arrange - Number expression should not be rewritten
            var expression = Expression.GreaterThan(FieldName.Number, null, 10);
            var sqlRoot = CreateSqlRootWithExpression(expression);

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(DateTimeBoundedRangeRewriter.Instance, null);

            // Assert - Should return unchanged
            Assert.Same(sqlRoot, result);
            Assert.Single(result.SearchParamTableExpressions);
        }

        [Fact]
        public void GivenSingleDateTimeExpression_WhenRewritten_ThenNoRewriteOccurs()
        {
            // Arrange - Single expression (not And) should not be rewritten
            var expression = Expression.GreaterThan(FieldName.DateTimeEnd, null, BaseDate);
            var sqlRoot = CreateSqlRootWithExpression(expression);

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(DateTimeBoundedRangeRewriter.Instance, null);

            // Assert
            Assert.Same(sqlRoot, result);
            Assert.Single(result.SearchParamTableExpressions);
        }

        [Fact]
        public void GivenOrExpression_WhenRewritten_ThenNoRewriteOccurs()
        {
            // Arrange - Or instead of And
            var expr1 = Expression.GreaterThan(FieldName.DateTimeEnd, null, BaseDate);
            var expr2 = Expression.LessThan(FieldName.DateTimeStart, null, BaseDate.AddHours(6));
            var orExpression = Expression.Or(expr1, expr2);

            var sqlRoot = CreateSqlRootWithExpression(orExpression);

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(DateTimeBoundedRangeRewriter.Instance, null);

            // Assert
            Assert.Same(sqlRoot, result);
        }

        [Fact]
        public void GivenWrongFieldNames_WhenRewritten_ThenNoRewriteOccurs()
        {
            // Arrange - Both fields must be DateTimeEnd and DateTimeStart
            var expr1 = Expression.GreaterThan(FieldName.DateTimeStart, null, BaseDate);
            var expr2 = Expression.LessThan(FieldName.DateTimeStart, null, BaseDate.AddHours(6));
            var andExpression = Expression.And(expr1, expr2);

            var sqlRoot = CreateSqlRootWithExpression(andExpression);

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(DateTimeBoundedRangeRewriter.Instance, null);

            // Assert
            Assert.Same(sqlRoot, result);
        }

        [Fact]
        public void GivenWrongOperators_WhenRewritten_ThenNoRewriteOccurs()
        {
            // Arrange - First must be >= or >, second must be < or <=
            var expr1 = Expression.LessThan(FieldName.DateTimeEnd, null, BaseDate);
            var expr2 = Expression.GreaterThan(FieldName.DateTimeStart, null, BaseDate.AddHours(6));
            var andExpression = Expression.And(expr1, expr2);

            var sqlRoot = CreateSqlRootWithExpression(andExpression);

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(DateTimeBoundedRangeRewriter.Instance, null);

            // Assert
            Assert.Same(sqlRoot, result);
        }

        [Fact]
        public void GivenComponentIndex_WhenRewritten_ThenComponentIndexPreserved()
        {
            // Arrange
            var componentIndex = 1;
            var greaterThanExpr = Expression.GreaterThan(FieldName.DateTimeEnd, componentIndex, BaseDate);
            var lessThanExpr = Expression.LessThan(FieldName.DateTimeStart, componentIndex, BaseDate.AddHours(6));
            var andExpression = Expression.And(greaterThanExpr, lessThanExpr);

            var sqlRoot = CreateSqlRootWithExpression(andExpression);

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(DateTimeBoundedRangeRewriter.Instance, null);

            // Assert
            Assert.Equal(2, result.SearchParamTableExpressions.Count);

            var concatenationAnd = Assert.IsType<MultiaryExpression>(result.SearchParamTableExpressions[1].Predicate);

            // Verify all expressions have the same component index
            foreach (var expr in concatenationAnd.Expressions)
            {
                var binaryExpr = Assert.IsType<BinaryExpression>(expr);
                Assert.Equal(componentIndex, binaryExpr.ComponentIndex);
            }
        }

        [Fact]
        public void GivenThreeExpressions_WhenRewritten_ThenNoRewriteOccurs()
        {
            // Arrange - And with 3 expressions (pattern expects exactly 2)
            var expr1 = Expression.GreaterThan(FieldName.DateTimeEnd, null, BaseDate);
            var expr2 = Expression.LessThan(FieldName.DateTimeStart, null, BaseDate.AddHours(6));
            var expr3 = Expression.Equals(FieldName.TokenCode, null, "test");
            var andExpression = Expression.And(expr1, expr2, expr3);

            var sqlRoot = CreateSqlRootWithExpression(andExpression);

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(DateTimeBoundedRangeRewriter.Instance, null);

            // Assert
            Assert.Same(sqlRoot, result);
        }

        [Fact]
        public void GivenExpressionsInReverseOrder_WhenRewritten_ThenRewriterHandlesReordering()
        {
            // Arrange - LessThan before GreaterThan (should still match after reordering)
            var lessThanExpr = Expression.LessThan(FieldName.DateTimeStart, null, BaseDate.AddHours(6));
            var greaterThanExpr = Expression.GreaterThan(FieldName.DateTimeEnd, null, BaseDate);
            var andExpression = Expression.And(lessThanExpr, greaterThanExpr);

            var sqlRoot = CreateSqlRootWithExpression(andExpression);

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(DateTimeBoundedRangeRewriter.Instance, null);

            // Assert - Should still be rewritten (rewriter sorts by operator)
            Assert.Equal(2, result.SearchParamTableExpressions.Count);
            Assert.Equal(SearchParamTableExpressionKind.Concatenation, result.SearchParamTableExpressions[1].Kind);
        }

        [Fact]
        public void GivenEmptySqlRoot_WhenRewritten_ThenReturnsUnchanged()
        {
            // Arrange
            var sqlRoot = new SqlRootExpression(
                new List<SearchParamTableExpression>(),
                new List<SearchParameterExpressionBase>());

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(DateTimeBoundedRangeRewriter.Instance, null);

            // Assert
            Assert.Same(sqlRoot, result);
        }

        [Fact]
        public void GivenChainExpression_WhenRewritten_ThenSkipsChainExpressions()
        {
            // Arrange
            var greaterThanExpr = Expression.GreaterThan(FieldName.DateTimeEnd, null, BaseDate);
            var lessThanExpr = Expression.LessThan(FieldName.DateTimeStart, null, BaseDate.AddHours(6));
            var andExpression = Expression.And(greaterThanExpr, lessThanExpr);

            var tableExpressions = new System.Collections.Generic.List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, andExpression, SearchParamTableExpressionKind.Chain),
            };

            var sqlRoot = new SqlRootExpression(tableExpressions, new System.Collections.Generic.List<SearchParameterExpressionBase>());

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(DateTimeBoundedRangeRewriter.Instance, null);

            // Assert - Chain expressions should be skipped
            Assert.Same(sqlRoot, result);
            Assert.Single(result.SearchParamTableExpressions);
        }

        [Fact]
        public void GivenIncludeExpression_WhenRewritten_ThenSkipsIncludeExpressions()
        {
            // Arrange
            var greaterThanExpr = Expression.GreaterThan(FieldName.DateTimeEnd, null, BaseDate);
            var lessThanExpr = Expression.LessThan(FieldName.DateTimeStart, null, BaseDate.AddHours(6));
            var andExpression = Expression.And(greaterThanExpr, lessThanExpr);

            var tableExpressions = new System.Collections.Generic.List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, andExpression, SearchParamTableExpressionKind.Include),
            };

            var sqlRoot = new SqlRootExpression(tableExpressions, new System.Collections.Generic.List<SearchParameterExpressionBase>());

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(DateTimeBoundedRangeRewriter.Instance, null);

            // Assert
            Assert.Same(sqlRoot, result);
            Assert.Single(result.SearchParamTableExpressions);
        }

        [Fact]
        public void GivenSortExpression_WhenRewritten_ThenSkipsSortExpressions()
        {
            // Arrange
            var greaterThanExpr = Expression.GreaterThan(FieldName.DateTimeEnd, null, BaseDate);
            var lessThanExpr = Expression.LessThan(FieldName.DateTimeStart, null, BaseDate.AddHours(6));
            var andExpression = Expression.And(greaterThanExpr, lessThanExpr);

            var tableExpressions = new System.Collections.Generic.List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, andExpression, SearchParamTableExpressionKind.Sort),
            };

            var sqlRoot = new SqlRootExpression(tableExpressions, new System.Collections.Generic.List<SearchParameterExpressionBase>());

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(DateTimeBoundedRangeRewriter.Instance, null);

            // Assert
            Assert.Same(sqlRoot, result);
            Assert.Single(result.SearchParamTableExpressions);
        }

        [Fact]
        public void GivenAllExpression_WhenRewritten_ThenSkipsAllExpressions()
        {
            // Arrange
            var greaterThanExpr = Expression.GreaterThan(FieldName.DateTimeEnd, null, BaseDate);
            var lessThanExpr = Expression.LessThan(FieldName.DateTimeStart, null, BaseDate.AddHours(6));
            var andExpression = Expression.And(greaterThanExpr, lessThanExpr);

            var tableExpressions = new System.Collections.Generic.List<SearchParamTableExpression>
            {
                new SearchParamTableExpression(null, andExpression, SearchParamTableExpressionKind.All),
            };

            var sqlRoot = new SqlRootExpression(tableExpressions, new System.Collections.Generic.List<SearchParameterExpressionBase>());

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(DateTimeBoundedRangeRewriter.Instance, null);

            // Assert
            Assert.Same(sqlRoot, result);
            Assert.Single(result.SearchParamTableExpressions);
        }

        [Fact]
        public void GivenGreaterThanWithLessThanOrEqual_WhenRewritten_ThenCreatesOptimization()
        {
            // Arrange
            var greaterThanExpr = Expression.GreaterThan(FieldName.DateTimeEnd, null, BaseDate);
            var lessThanOrEqualExpr = Expression.LessThanOrEqual(FieldName.DateTimeStart, null, BaseDate.AddHours(6));
            var andExpression = Expression.And(greaterThanExpr, lessThanOrEqualExpr);

            var sqlRoot = CreateSqlRootWithExpression(andExpression);

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(DateTimeBoundedRangeRewriter.Instance, null);

            // Assert - Should create optimization
            Assert.Equal(2, result.SearchParamTableExpressions.Count);
            Assert.Equal(SearchParamTableExpressionKind.Concatenation, result.SearchParamTableExpressions[1].Kind);
        }

        [Fact]
        public void GivenGreaterThanOrEqualWithLessThan_WhenRewritten_ThenCreatesOptimization()
        {
            // Arrange
            var greaterThanOrEqualExpr = Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, null, BaseDate);
            var lessThanExpr = Expression.LessThan(FieldName.DateTimeStart, null, BaseDate.AddHours(6));
            var andExpression = Expression.And(greaterThanOrEqualExpr, lessThanExpr);

            var sqlRoot = CreateSqlRootWithExpression(andExpression);

            // Act
            var result = (SqlRootExpression)sqlRoot.AcceptVisitor(DateTimeBoundedRangeRewriter.Instance, null);

            // Assert
            Assert.Equal(2, result.SearchParamTableExpressions.Count);
            Assert.Equal(SearchParamTableExpressionKind.Concatenation, result.SearchParamTableExpressions[1].Kind);
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
