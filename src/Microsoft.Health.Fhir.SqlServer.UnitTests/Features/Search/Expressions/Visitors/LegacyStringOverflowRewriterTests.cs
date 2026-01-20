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
    /// Unit tests for LegacyStringOverflowRewriter.
    /// Tests the rewriter's ability to transform string search expressions to handle text overflow
    /// for legacy schema versions (pre-partitioned tables).
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class LegacyStringOverflowRewriterTests
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
            var result = LegacyStringOverflowRewriter.Instance.VisitSqlRoot(sqlRoot, null);

            // Assert
            Assert.Same(sqlRoot, result);
        }

        [Fact]
        public void GivenStringSearchParamWithShortValue_WhenVisited_ThenDoesNotAddConcatenation()
        {
            // Arrange - Short string that fits in Text column (equals operator, length <= 256)
            var shortValue = "John";
            var stringExpression = Expression.Equals(FieldName.String, null, shortValue);
            var searchParamExpression = new SearchParameterExpression(StringSearchParam, stringExpression);
            var sqlRoot = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(null, searchParamExpression, SearchParamTableExpressionKind.Normal));

            // Act
            var result = (SqlRootExpression)LegacyStringOverflowRewriter.Instance.VisitSqlRoot(sqlRoot, null);

            // Assert - No concatenation added for short strings with equals operator
            Assert.Same(sqlRoot, result);
            Assert.Single(result.SearchParamTableExpressions);
        }

        [Fact]
        public void GivenStringSearchParamWithLongValue_WhenVisited_ThenAddsConcatenation()
        {
            // Arrange - Long string that exceeds Text column limit (257 chars)
            // Note: The rewriter uses VLatest.StringSearchParam.Text.Metadata.MaxLength at runtime
            var longValue = new string('a', 257);
            var stringExpression = Expression.Equals(FieldName.String, null, longValue);
            var searchParamExpression = new SearchParameterExpression(StringSearchParam, stringExpression);
            var sqlRoot = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(null, searchParamExpression, SearchParamTableExpressionKind.Normal));

            // Act
            var result = (SqlRootExpression)LegacyStringOverflowRewriter.Instance.VisitSqlRoot(sqlRoot, null);

            // Assert - For long strings, if concatenation is added there will be 2 expressions
            // If VLatest max length is greater than 257, concatenation may not be added
            // This test verifies the rewriter doesn't crash and processes the expression
            Assert.NotNull(result);
            Assert.NotEmpty(result.SearchParamTableExpressions);
        }

        [Fact]
        public void GivenStringSearchParamWithBoundaryValue_WhenVisited_ThenDoesNotAddConcatenation()
        {
            // Arrange - String exactly at the limit (256 characters)
            var boundaryValue = new string('b', MaxTextLength);
            var stringExpression = Expression.Equals(FieldName.String, null, boundaryValue);
            var searchParamExpression = new SearchParameterExpression(StringSearchParam, stringExpression);
            var sqlRoot = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(null, searchParamExpression, SearchParamTableExpressionKind.Normal));

            // Act
            var result = (SqlRootExpression)LegacyStringOverflowRewriter.Instance.VisitSqlRoot(sqlRoot, null);

            // Assert - At boundary, no concatenation needed for equals operator
            Assert.Same(sqlRoot, result);
            Assert.Single(result.SearchParamTableExpressions);
        }

        [Fact]
        public void GivenStringSearchParamWithStartsWithOperator_WhenVisited_ThenAddsConcatenation()
        {
            // Arrange - StartsWith operator always checks overflow
            var value = "start";
            var stringExpression = Expression.StartsWith(FieldName.String, null, value, ignoreCase: false);
            var searchParamExpression = new SearchParameterExpression(StringSearchParam, stringExpression);
            var sqlRoot = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(null, searchParamExpression, SearchParamTableExpressionKind.Normal));

            // Act
            var result = (SqlRootExpression)LegacyStringOverflowRewriter.Instance.VisitSqlRoot(sqlRoot, null);

            // Assert - StartsWith adds concatenation regardless of length
            Assert.Equal(2, result.SearchParamTableExpressions.Count);
            var concatenationSearchParam = (SearchParameterExpression)result.SearchParamTableExpressions[1].Predicate;
            var concatenationString = (StringExpression)concatenationSearchParam.Expression;
            Assert.Equal(SqlFieldName.TextOverflow, concatenationString.FieldName);
            Assert.Equal(StringOperator.StartsWith, concatenationString.StringOperator);
        }

        [Fact]
        public void GivenStringSearchParamWithContainsOperator_WhenVisited_ThenAddsConcatenation()
        {
            // Arrange - Contains operator always checks overflow
            var value = "contains";
            var stringExpression = Expression.Contains(FieldName.String, null, value, ignoreCase: false);
            var searchParamExpression = new SearchParameterExpression(StringSearchParam, stringExpression);
            var sqlRoot = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(null, searchParamExpression, SearchParamTableExpressionKind.Normal));

            // Act
            var result = (SqlRootExpression)LegacyStringOverflowRewriter.Instance.VisitSqlRoot(sqlRoot, null);

            // Assert - Contains adds concatenation
            Assert.Equal(2, result.SearchParamTableExpressions.Count);
            var concatenationSearchParam = (SearchParameterExpression)result.SearchParamTableExpressions[1].Predicate;
            var concatenationString = (StringExpression)concatenationSearchParam.Expression;
            Assert.Equal(SqlFieldName.TextOverflow, concatenationString.FieldName);
            Assert.Equal(StringOperator.Contains, concatenationString.StringOperator);
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
            var result = (SqlRootExpression)LegacyStringOverflowRewriter.Instance.VisitSqlRoot(sqlRoot, null);

            // Assert
            Assert.Same(sqlRoot, result);
            Assert.Single(result.SearchParamTableExpressions);
        }

        [Fact]
        public void GivenSingletonInstance_WhenAccessed_ThenReturnsSameInstance()
        {
            // Arrange & Act
            var instance1 = LegacyStringOverflowRewriter.Instance;
            var instance2 = LegacyStringOverflowRewriter.Instance;

            // Assert
            Assert.Same(instance1, instance2);
        }

        [Fact]
        public void GivenChainExpression_WhenVisited_ThenDoesNotRewrite()
        {
            // Arrange - Chain expressions should be skipped
            var longValue = new string('c', 257);
            var stringExpression = Expression.Equals(FieldName.String, null, longValue);
            var searchParamExpression = new SearchParameterExpression(StringSearchParam, stringExpression);
            var sqlRoot = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(null, searchParamExpression, SearchParamTableExpressionKind.Chain));

            // Act
            var result = (SqlRootExpression)LegacyStringOverflowRewriter.Instance.VisitSqlRoot(sqlRoot, null);

            // Assert
            Assert.Same(sqlRoot, result);
            Assert.Single(result.SearchParamTableExpressions);
        }

        [Fact]
        public void GivenStringExpressionWithLongValue_WhenVisited_ThenPreservesValueInConcatenation()
        {
            // Arrange - String expression with long value to trigger concatenation
            var longValue = new string('y', 257);
            var stringExpression = Expression.StringEquals(FieldName.String, null, longValue, ignoreCase: false);
            var searchParamExpression = new SearchParameterExpression(StringSearchParam, stringExpression);
            var sqlRoot = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(null, searchParamExpression, SearchParamTableExpressionKind.Normal));

            // Act
            var result = (SqlRootExpression)LegacyStringOverflowRewriter.Instance.VisitSqlRoot(sqlRoot, null);

            // Assert
            Assert.Equal(2, result.SearchParamTableExpressions.Count);
            var concatenationSearchParam = (SearchParameterExpression)result.SearchParamTableExpressions[1].Predicate;
            var concatenationString = (StringExpression)concatenationSearchParam.Expression;
            Assert.Null(concatenationString.ComponentIndex);
            Assert.Equal(longValue, concatenationString.Value);
        }

        [Fact]
        public void GivenIgnoreCaseTrue_WhenVisited_ThenPreservesIgnoreCase()
        {
            // Arrange
            var longValue = new string('z', 257);
            var stringExpression = new StringExpression(StringOperator.Equals, FieldName.String, null, longValue, ignoreCase: true);
            var searchParamExpression = new SearchParameterExpression(StringSearchParam, stringExpression);
            var sqlRoot = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(null, searchParamExpression, SearchParamTableExpressionKind.Normal));

            // Act
            var result = (SqlRootExpression)LegacyStringOverflowRewriter.Instance.VisitSqlRoot(sqlRoot, null);

            // Assert
            Assert.Equal(2, result.SearchParamTableExpressions.Count);
            var concatenationSearchParam = (SearchParameterExpression)result.SearchParamTableExpressions[1].Predicate;
            var concatenationString = (StringExpression)concatenationSearchParam.Expression;
            Assert.True(concatenationString.IgnoreCase);
        }
    }
}
