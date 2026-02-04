// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// Unit tests for StringOverflowRewriter.
    /// Tests the rewriter's ability to transform string search expressions to handle text overflow
    /// for modern schema versions (partitioned tables and above).
    /// Unlike LegacyStringOverflowRewriter which uses concatenation, this rewriter uses AND/OR logic.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class StringOverflowRewriterTests
    {
        private static readonly SearchParameterInfo StringSearchParam = new SearchParameterInfo(
            name: "name",
            code: "name",
            searchParamType: SearchParamType.String,
            url: new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));

        private static readonly SearchParameterInfo TokenSearchParam = new SearchParameterInfo(
            name: "status",
            code: "status",
            searchParamType: SearchParamType.Token,
            url: new Uri("http://hl7.org/fhir/SearchParameter/Patient-status"));

        private const int MaxTextLength = 256; // VLatest.StringSearchParam.Text.Metadata.MaxLength

        [Fact]
        public void GivenEmptySearchParamTableExpressions_WhenVisited_ThenReturnsUnchanged()
        {
            // Arrange
            var sqlRoot = new SqlRootExpression(
                Array.Empty<SearchParamTableExpression>(),
                Array.Empty<SearchParameterExpressionBase>());

            // Act
            var result = StringOverflowRewriter.Instance.VisitSqlRoot(sqlRoot, null);

            // Assert
            Assert.Same(sqlRoot, result);
        }

        [Fact]
        public void GivenStringSearchParamWithShortValue_WhenEqualsOperator_ThenReturnsUnchanged()
        {
            // Arrange - Short string that fits in Text column
            var shortValue = "John";
            var stringExpression = Expression.StringEquals(FieldName.String, null, shortValue, ignoreCase: false);
            var searchParamExpression = new SearchParameterExpression(StringSearchParam, stringExpression);
            var sqlRoot = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(null, searchParamExpression, SearchParamTableExpressionKind.Normal));

            // Act
            var result = (SqlRootExpression)StringOverflowRewriter.Instance.VisitSqlRoot(sqlRoot, null);

            // Assert - No rewrite for short strings with equals operator
            Assert.Same(sqlRoot, result);
        }

        [Fact]
        public void GivenStringSearchParamWithLongValue_WhenEqualsOperator_ThenCreatesAndExpression()
        {
            // Arrange - Long string that exceeds Text column limit
            var longValue = new string('a', 257);
            var stringExpression = Expression.StringEquals(FieldName.String, null, longValue, ignoreCase: false);
            var searchParamExpression = new SearchParameterExpression(StringSearchParam, stringExpression);
            var sqlRoot = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(null, searchParamExpression, SearchParamTableExpressionKind.Normal));

            // Act
            var result = (SqlRootExpression)StringOverflowRewriter.Instance.VisitSqlRoot(sqlRoot, null);

            // Assert - Should create AND expression with prefix check and overflow check
            var rewrittenSearchParam = (SearchParameterExpression)result.SearchParamTableExpressions[0].Predicate;
            var andExpression = rewrittenSearchParam.Expression as MultiaryExpression;

            Assert.NotNull(andExpression);
            Assert.Equal(MultiaryOperator.And, andExpression.MultiaryOperation);
            Assert.Equal(2, andExpression.Expressions.Count);

            // First expression should check Text column with prefix
            var prefixExpression = andExpression.Expressions[0] as StringExpression;
            Assert.NotNull(prefixExpression);
            Assert.Equal(FieldName.String, prefixExpression!.FieldName);
            Assert.Equal(MaxTextLength, prefixExpression.Value.Length);

            // Second expression should check TextOverflow column with full value
            var overflowExpression = andExpression.Expressions[1] as StringExpression;
            Assert.NotNull(overflowExpression);
            Assert.Equal(SqlFieldName.TextOverflow, overflowExpression!.FieldName);
            Assert.Equal(longValue, overflowExpression.Value);
        }

        [Fact]
        public void GivenStringSearchParamWithBoundaryValue_WhenEqualsOperator_ThenReturnsUnchanged()
        {
            // Arrange - String exactly at the limit
            var boundaryValue = new string('b', MaxTextLength);
            var stringExpression = Expression.StringEquals(FieldName.String, null, boundaryValue, ignoreCase: false);
            var searchParamExpression = new SearchParameterExpression(StringSearchParam, stringExpression);
            var sqlRoot = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(null, searchParamExpression, SearchParamTableExpressionKind.Normal));

            // Act
            var result = (SqlRootExpression)StringOverflowRewriter.Instance.VisitSqlRoot(sqlRoot, null);

            // Assert - At boundary, no rewrite needed
            Assert.Same(sqlRoot, result);
        }

        [Fact]
        public void GivenStringSearchParamWithLongValue_WhenStartsWithOperator_ThenCreatesAndExpression()
        {
            // Arrange - Long string with StartsWith operator
            var longValue = new string('s', 257);
            var stringExpression = Expression.StartsWith(FieldName.String, null, longValue, ignoreCase: false);
            var searchParamExpression = new SearchParameterExpression(StringSearchParam, stringExpression);
            var sqlRoot = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(null, searchParamExpression, SearchParamTableExpressionKind.Normal));

            // Act
            var result = (SqlRootExpression)StringOverflowRewriter.Instance.VisitSqlRoot(sqlRoot, null);

            // Assert - Should create AND expression
            var rewrittenSearchParam = (SearchParameterExpression)result.SearchParamTableExpressions[0].Predicate;
            var andExpression = rewrittenSearchParam.Expression as MultiaryExpression;

            Assert.NotNull(andExpression);
            Assert.Equal(MultiaryOperator.And, andExpression!.MultiaryOperation);
            Assert.Equal(2, andExpression.Expressions.Count);

            // Verify both expressions use StartsWith operator
            var prefixExpression = andExpression.Expressions[0] as StringExpression;
            Assert.NotNull(prefixExpression);
            Assert.Equal(StringOperator.StartsWith, prefixExpression!.StringOperator);

            var overflowExpression = andExpression.Expressions[1] as StringExpression;
            Assert.NotNull(overflowExpression);
            Assert.Equal(StringOperator.StartsWith, overflowExpression!.StringOperator);
        }

        [Fact]
        public void GivenStringSearchParamWithShortValue_WhenStartsWithOperator_ThenReturnsUnchanged()
        {
            // Arrange - Short string with StartsWith operator
            var shortValue = "start";
            var stringExpression = Expression.StartsWith(FieldName.String, null, shortValue, ignoreCase: false);
            var searchParamExpression = new SearchParameterExpression(StringSearchParam, stringExpression);
            var sqlRoot = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(null, searchParamExpression, SearchParamTableExpressionKind.Normal));

            // Act
            var result = (SqlRootExpression)StringOverflowRewriter.Instance.VisitSqlRoot(sqlRoot, null);

            // Assert - Short StartsWith doesn't need overflow handling
            Assert.Same(sqlRoot, result);
        }

        [Fact]
        public void GivenStringSearchParamWithContainsOperator_ThenCreatesOrExpression()
        {
            // Arrange - Contains operator should always check overflow
            var value = "contains";
            var stringExpression = Expression.Contains(FieldName.String, null, value, ignoreCase: false);
            var searchParamExpression = new SearchParameterExpression(StringSearchParam, stringExpression);
            var sqlRoot = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(null, searchParamExpression, SearchParamTableExpressionKind.Normal));

            // Act
            var result = (SqlRootExpression)StringOverflowRewriter.Instance.VisitSqlRoot(sqlRoot, null);

            // Assert - Should create OR expression
            var rewrittenSearchParam = (SearchParameterExpression)result.SearchParamTableExpressions[0].Predicate;
            var orExpression = rewrittenSearchParam.Expression as MultiaryExpression;

            Assert.NotNull(orExpression);
            Assert.Equal(MultiaryOperator.Or, orExpression!.MultiaryOperation);
            Assert.Equal(2, orExpression.Expressions.Count);

            // First expression should check Text column
            var textExpression = orExpression.Expressions[0] as StringExpression;
            Assert.NotNull(textExpression);
            Assert.Equal(FieldName.String, textExpression!.FieldName);

            // Second expression should check TextOverflow column
            var overflowExpression = orExpression.Expressions[1] as StringExpression;
            Assert.NotNull(overflowExpression);
            Assert.Equal(SqlFieldName.TextOverflow, overflowExpression!.FieldName);
        }

        [Fact]
        public void GivenNonStringSearchParam_WhenVisited_ThenReturnsUnchanged()
        {
            // Arrange - Token search parameter should not be rewritten
            var tokenExpression = Expression.Equals(FieldName.TokenCode, null, "code");
            var searchParamExpression = new SearchParameterExpression(TokenSearchParam, tokenExpression);
            var sqlRoot = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(null, searchParamExpression, SearchParamTableExpressionKind.Normal));

            // Act
            var result = (SqlRootExpression)StringOverflowRewriter.Instance.VisitSqlRoot(sqlRoot, null);

            // Assert
            Assert.Same(sqlRoot, result);
        }

        [Fact]
        public void GivenSingletonInstance_WhenAccessed_ThenReturnsSameInstance()
        {
            // Arrange & Act
            var instance1 = StringOverflowRewriter.Instance;
            var instance2 = StringOverflowRewriter.Instance;

            // Assert
            Assert.Same(instance1, instance2);
        }

        [Fact]
        public void GivenTokenCodeFieldName_WhenVisited_ThenReturnsUnchanged()
        {
            // Arrange - TokenCode field should not be rewritten even with string search param
            var stringExpression = new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, "code", ignoreCase: false);
            var searchParamExpression = new SearchParameterExpression(StringSearchParam, stringExpression);
            var sqlRoot = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(null, searchParamExpression, SearchParamTableExpressionKind.Normal));

            // Act
            var result = (SqlRootExpression)StringOverflowRewriter.Instance.VisitSqlRoot(sqlRoot, null);

            // Assert
            Assert.Same(sqlRoot, result);
        }

        [Fact]
        public void GivenIgnoreCaseTrue_WhenLongValueWithEquals_ThenPreservesIgnoreCase()
        {
            // Arrange
            var longValue = new string('z', 257);
            var stringExpression = new StringExpression(StringOperator.Equals, FieldName.String, null, longValue, ignoreCase: true);
            var searchParamExpression = new SearchParameterExpression(StringSearchParam, stringExpression);
            var sqlRoot = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(null, searchParamExpression, SearchParamTableExpressionKind.Normal));

            // Act
            var result = (SqlRootExpression)StringOverflowRewriter.Instance.VisitSqlRoot(sqlRoot, null);

            // Assert
            var rewrittenSearchParam = (SearchParameterExpression)result.SearchParamTableExpressions[0].Predicate;
            var andExpression = rewrittenSearchParam.Expression as MultiaryExpression;
            Assert.NotNull(andExpression);

            var prefixExpression = andExpression!.Expressions[0] as StringExpression;
            Assert.NotNull(prefixExpression);
            Assert.True(prefixExpression!.IgnoreCase);

            var overflowExpression = andExpression.Expressions[1] as StringExpression;
            Assert.NotNull(overflowExpression);
            Assert.True(overflowExpression!.IgnoreCase);
        }

        [Fact]
        public void GivenComponentIndex_WhenLongValueWithEquals_ThenPreservesComponentIndex()
        {
            // Arrange
            var longValue = new string('c', 257);
            var stringExpression = new StringExpression(StringOperator.Equals, FieldName.String, componentIndex: 2, longValue, ignoreCase: false);
            var searchParamExpression = new SearchParameterExpression(StringSearchParam, stringExpression);
            var sqlRoot = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(null, searchParamExpression, SearchParamTableExpressionKind.Normal));

            // Act
            var result = (SqlRootExpression)StringOverflowRewriter.Instance.VisitSqlRoot(sqlRoot, null);

            // Assert
            var rewrittenSearchParam = (SearchParameterExpression)result.SearchParamTableExpressions[0].Predicate;
            var andExpression = rewrittenSearchParam.Expression as MultiaryExpression;
            Assert.NotNull(andExpression);

            var prefixExpression = andExpression!.Expressions[0] as StringExpression;
            Assert.NotNull(prefixExpression);
            Assert.Equal(2, prefixExpression!.ComponentIndex);

            var overflowExpression = andExpression.Expressions[1] as StringExpression;
            Assert.NotNull(overflowExpression);
            Assert.Equal(2, overflowExpression!.ComponentIndex);
        }

        [Fact]
        public void GivenMultipleSearchParamTableExpressions_WhenVisited_ThenRewritesOnlyStringParams()
        {
            // Arrange - Mix of string and token search parameters
            var shortStringExpression = Expression.StringEquals(FieldName.String, null, "short", ignoreCase: false);
            var longStringExpression = Expression.StringEquals(FieldName.String, null, new string('x', 257), ignoreCase: false);
            var tokenExpression = Expression.Equals(FieldName.TokenCode, null, "code");

            var stringSearchParam1 = new SearchParameterExpression(StringSearchParam, shortStringExpression);
            var stringSearchParam2 = new SearchParameterExpression(StringSearchParam, longStringExpression);
            var tokenSearchParam = new SearchParameterExpression(TokenSearchParam, tokenExpression);

            var sqlRoot = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(null, stringSearchParam1, SearchParamTableExpressionKind.Normal),
                new SearchParamTableExpression(null, stringSearchParam2, SearchParamTableExpressionKind.Normal),
                new SearchParamTableExpression(null, tokenSearchParam, SearchParamTableExpressionKind.Normal));

            // Act
            var result = (SqlRootExpression)StringOverflowRewriter.Instance.VisitSqlRoot(sqlRoot, null);

            // Assert
            Assert.Equal(3, result.SearchParamTableExpressions.Count);

            // First should be unchanged (short string)
            var first = (SearchParameterExpression)result.SearchParamTableExpressions[0].Predicate;
            Assert.IsType<StringExpression>(first.Expression);

            // Second should be rewritten (long string)
            var second = (SearchParameterExpression)result.SearchParamTableExpressions[1].Predicate;
            Assert.IsType<MultiaryExpression>(second.Expression);

            // Third should be unchanged (token)
            var third = (SearchParameterExpression)result.SearchParamTableExpressions[2].Predicate;
            Assert.IsType<BinaryExpression>(third.Expression);
        }

        [Fact]
        public void GivenEndsWithOperator_WhenVisited_ThenThrowsInvalidOperationException()
        {
            // Arrange - EndsWith is not supported by StringOverflowRewriter
            var value = "end";
            var stringExpression = Expression.EndsWith(FieldName.String, null, value, ignoreCase: false);
            var searchParamExpression = new SearchParameterExpression(StringSearchParam, stringExpression);
            var sqlRoot = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(null, searchParamExpression, SearchParamTableExpressionKind.Normal));

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => StringOverflowRewriter.Instance.VisitSqlRoot(sqlRoot, null));
        }

        [Fact]
        public void GivenNotStartsWithOperator_WhenVisited_ThenThrowsInvalidOperationException()
        {
            // Arrange - NotStartsWith is not supported by StringOverflowRewriter
            var value = "notstart";
            var stringExpression = Expression.NotStartsWith(FieldName.String, null, value, ignoreCase: false);
            var searchParamExpression = new SearchParameterExpression(StringSearchParam, stringExpression);
            var sqlRoot = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(null, searchParamExpression, SearchParamTableExpressionKind.Normal));

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => StringOverflowRewriter.Instance.VisitSqlRoot(sqlRoot, null));
        }

        [Fact]
        public void GivenNotContainsOperator_WhenVisited_ThenThrowsInvalidOperationException()
        {
            // Arrange - NotContains is not supported by StringOverflowRewriter
            var value = "notcontains";
            var stringExpression = Expression.NotContains(FieldName.String, null, value, ignoreCase: false);
            var searchParamExpression = new SearchParameterExpression(StringSearchParam, stringExpression);
            var sqlRoot = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(null, searchParamExpression, SearchParamTableExpressionKind.Normal));

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => StringOverflowRewriter.Instance.VisitSqlRoot(sqlRoot, null));
        }

        [Fact]
        public void GivenNotEndsWithOperator_WhenVisited_ThenThrowsInvalidOperationException()
        {
            // Arrange - NotEndsWith is not supported by StringOverflowRewriter
            var value = "notend";
            var stringExpression = Expression.NotEndsWith(FieldName.String, null, value, ignoreCase: false);
            var searchParamExpression = new SearchParameterExpression(StringSearchParam, stringExpression);
            var sqlRoot = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(null, searchParamExpression, SearchParamTableExpressionKind.Normal));

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => StringOverflowRewriter.Instance.VisitSqlRoot(sqlRoot, null));
        }

        [Fact]
        public void GivenContainsWithLongValue_WhenVisited_ThenBothExpressionsHaveSameValue()
        {
            // Arrange - Contains with long value
            var longValue = new string('l', 300);
            var stringExpression = Expression.Contains(FieldName.String, null, longValue, ignoreCase: false);
            var searchParamExpression = new SearchParameterExpression(StringSearchParam, stringExpression);
            var sqlRoot = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(null, searchParamExpression, SearchParamTableExpressionKind.Normal));

            // Act
            var result = (SqlRootExpression)StringOverflowRewriter.Instance.VisitSqlRoot(sqlRoot, null);

            // Assert - Both OR branches should have the same full value
            var rewrittenSearchParam = (SearchParameterExpression)result.SearchParamTableExpressions[0].Predicate;
            var orExpression = rewrittenSearchParam.Expression as MultiaryExpression;
            Assert.NotNull(orExpression);

            var textExpression = orExpression!.Expressions[0] as StringExpression;
            Assert.NotNull(textExpression);
            Assert.Equal(longValue, textExpression!.Value);

            var overflowExpression = orExpression.Expressions[1] as StringExpression;
            Assert.NotNull(overflowExpression);
            Assert.Equal(longValue, overflowExpression!.Value);
        }
    }
}
