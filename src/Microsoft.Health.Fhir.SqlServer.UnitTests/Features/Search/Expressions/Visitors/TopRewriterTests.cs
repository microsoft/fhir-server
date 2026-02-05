// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// Unit tests for TopRewriter.
    /// Tests the rewriter's ability to add TOP expression to SQL queries.
    /// The TOP expression is added to limit the number of results returned by the query.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class TopRewriterTests
    {
        private static readonly SearchParameterInfo TestSearchParam = new SearchParameterInfo(
            name: "name",
            code: "name",
            searchParamType: SearchParamType.String,
            url: new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));

        [Fact]
        public void GivenCountOnlyQuery_WhenVisited_ThenReturnsUnchanged()
        {
            // Arrange
            var stringExpression = Expression.StringEquals(FieldName.String, null, "test", ignoreCase: false);
            var searchParamExpression = new SearchParameterExpression(TestSearchParam, stringExpression);
            var sqlRoot = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(null, searchParamExpression, SearchParamTableExpressionKind.Normal));

            var searchOptions = new SearchOptions { CountOnly = true };

            // Act
            var result = TopRewriter.Instance.VisitSqlRoot(sqlRoot, searchOptions);

            // Assert - Should return unchanged for count-only queries
            Assert.Same(sqlRoot, result);
            Assert.Single(((SqlRootExpression)result).SearchParamTableExpressions);
        }

        [Fact]
        public void GivenEmptySearchParamTableExpressions_WhenVisited_ThenReturnsUnchanged()
        {
            // Arrange
            var sqlRoot = new SqlRootExpression(
                Array.Empty<SearchParamTableExpression>(),
                Array.Empty<SearchParameterExpressionBase>());

            var searchOptions = new SearchOptions { CountOnly = false };

            // Act
            var result = TopRewriter.Instance.VisitSqlRoot(sqlRoot, searchOptions);

            // Assert - Should return unchanged when no search param table expressions exist
            Assert.Same(sqlRoot, result);
            Assert.Empty(((SqlRootExpression)result).SearchParamTableExpressions);
        }

        [Fact]
        public void GivenNormalQuery_WhenVisited_ThenAddsTopExpression()
        {
            // Arrange
            var stringExpression = Expression.StringEquals(FieldName.String, null, "test", ignoreCase: false);
            var searchParamExpression = new SearchParameterExpression(TestSearchParam, stringExpression);
            var sqlRoot = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(null, searchParamExpression, SearchParamTableExpressionKind.Normal));

            var searchOptions = new SearchOptions { CountOnly = false };

            // Act
            var result = (SqlRootExpression)TopRewriter.Instance.VisitSqlRoot(sqlRoot, searchOptions);

            // Assert - Should add TOP expression
            Assert.NotSame(sqlRoot, result);
            Assert.Equal(2, result.SearchParamTableExpressions.Count);

            // First expression should be the original
            Assert.Same(searchParamExpression, result.SearchParamTableExpressions[0].Predicate);
            Assert.Equal(SearchParamTableExpressionKind.Normal, result.SearchParamTableExpressions[0].Kind);

            // Second expression should be TOP
            Assert.Equal(SearchParamTableExpressionKind.Top, result.SearchParamTableExpressions[1].Kind);
            Assert.Null(result.SearchParamTableExpressions[1].Predicate);
        }

        [Fact]
        public void GivenQueryWithMultipleExpressions_WhenVisited_ThenAddsTopExpressionAtEnd()
        {
            // Arrange
            var expression1 = Expression.StringEquals(FieldName.String, null, "test1", ignoreCase: false);
            var expression2 = Expression.StringEquals(FieldName.String, null, "test2", ignoreCase: false);
            var searchParam1 = new SearchParameterExpression(TestSearchParam, expression1);
            var searchParam2 = new SearchParameterExpression(TestSearchParam, expression2);

            var sqlRoot = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(null, searchParam1, SearchParamTableExpressionKind.Normal),
                new SearchParamTableExpression(null, searchParam2, SearchParamTableExpressionKind.Normal));

            var searchOptions = new SearchOptions { CountOnly = false };

            // Act
            var result = (SqlRootExpression)TopRewriter.Instance.VisitSqlRoot(sqlRoot, searchOptions);

            // Assert - Should have 3 expressions (2 original + 1 TOP)
            Assert.Equal(3, result.SearchParamTableExpressions.Count);

            // First two should be unchanged
            Assert.Same(searchParam1, result.SearchParamTableExpressions[0].Predicate);
            Assert.Same(searchParam2, result.SearchParamTableExpressions[1].Predicate);

            // Last should be TOP
            Assert.Equal(SearchParamTableExpressionKind.Top, result.SearchParamTableExpressions[2].Kind);
            Assert.Null(result.SearchParamTableExpressions[2].Predicate);
        }

        [Fact]
        public void GivenQueryWithResourceTableExpressions_WhenVisited_ThenPreservesResourceTableExpressions()
        {
            // Arrange
            var stringExpression = Expression.StringEquals(FieldName.String, null, "test", ignoreCase: false);
            var searchParamExpression = new SearchParameterExpression(TestSearchParam, stringExpression);
            var resourceTableExpression = new SearchParameterExpression(
                SearchParameterInfo.ResourceTypeSearchParameter,
                Expression.StringEquals(FieldName.String, null, "Patient", ignoreCase: false));

            var sqlRoot = new SqlRootExpression(
                new[] { new SearchParamTableExpression(null, searchParamExpression, SearchParamTableExpressionKind.Normal) },
                new SearchParameterExpressionBase[] { resourceTableExpression });

            var searchOptions = new SearchOptions { CountOnly = false };

            // Act
            var result = (SqlRootExpression)TopRewriter.Instance.VisitSqlRoot(sqlRoot, searchOptions);

            // Assert - Resource table expressions should be preserved
            Assert.Equal(2, result.SearchParamTableExpressions.Count);
            Assert.Single(result.ResourceTableExpressions);
            Assert.Same(resourceTableExpression, result.ResourceTableExpressions[0]);
        }

        [Fact]
        public void GivenNullSearchOptions_WhenVisited_ThenThrowsArgumentNullException()
        {
            // Arrange
            var stringExpression = Expression.StringEquals(FieldName.String, null, "test", ignoreCase: false);
            var searchParamExpression = new SearchParameterExpression(TestSearchParam, stringExpression);
            var sqlRoot = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(null, searchParamExpression, SearchParamTableExpressionKind.Normal));

            // Act & Assert
            Assert.Throws<NullReferenceException>(() => TopRewriter.Instance.VisitSqlRoot(sqlRoot, null));
        }

        [Fact]
        public void GivenQueryWithDifferentExpressionKinds_WhenVisited_ThenAddsTopExpression()
        {
            // Arrange - Mix of different expression kinds
            var normalExpression = Expression.StringEquals(FieldName.String, null, "test", ignoreCase: false);
            var searchParam = new SearchParameterExpression(TestSearchParam, normalExpression);

            var sqlRoot = SqlRootExpression.WithSearchParamTableExpressions(
                new SearchParamTableExpression(null, searchParam, SearchParamTableExpressionKind.Normal),
                new SearchParamTableExpression(null, searchParam, SearchParamTableExpressionKind.Sort));

            var searchOptions = new SearchOptions { CountOnly = false };

            // Act
            var result = (SqlRootExpression)TopRewriter.Instance.VisitSqlRoot(sqlRoot, searchOptions);

            // Assert - Should add TOP expression at the end
            Assert.Equal(3, result.SearchParamTableExpressions.Count);
            Assert.Equal(SearchParamTableExpressionKind.Normal, result.SearchParamTableExpressions[0].Kind);
            Assert.Equal(SearchParamTableExpressionKind.Sort, result.SearchParamTableExpressions[1].Kind);
            Assert.Equal(SearchParamTableExpressionKind.Top, result.SearchParamTableExpressions[2].Kind);
        }
    }
}
