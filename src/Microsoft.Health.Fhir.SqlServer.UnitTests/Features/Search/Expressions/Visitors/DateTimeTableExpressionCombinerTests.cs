// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// Unit tests for DateTimeTableExpressionCombiner.
    /// Tests the logic that combines DateTime search parameter table expressions into more efficient queries.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class DateTimeTableExpressionCombinerTests
    {
        private static readonly SearchParameterInfo DateSearchParam = new SearchParameterInfo(
            name: "issued",
            code: "issued",
            searchParamType: SearchParamType.Date,
            url: new Uri("http://hl7.org/fhir/SearchParameter/Observation-issued"));

        private static readonly SearchParameterInfo TokenSearchParam = new SearchParameterInfo(
            name: "status",
            code: "status",
            searchParamType: SearchParamType.Token,
            url: new Uri("http://hl7.org/fhir/SearchParameter/Observation-status"));

        private static readonly DateTimeOffset TestStartDate = new DateTimeOffset(2024, 4, 22, 0, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset TestEndDate = new DateTimeOffset(2024, 4, 23, 0, 0, 0, TimeSpan.Zero);

        [Fact]
        public void GivenEmptySearchParamTableExpressions_WhenVisitSqlRoot_ThenReturnsUnchangedExpression()
        {
            var rootExpression = new SqlRootExpression(
                Array.Empty<SearchParamTableExpression>(),
                Array.Empty<SearchParameterExpressionBase>());

            var result = DateTimeTableExpressionCombiner.Instance.VisitSqlRoot(rootExpression, null);

            Assert.Same(rootExpression, result);
        }

        [Fact]
        public void GivenOneSearchParamTableExpression_WhenVisitSqlRoot_ThenReturnsUnchangedExpression()
        {
            var searchParamExpression = new SearchParameterExpression(
                DateSearchParam,
                Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, null, TestStartDate));

            var tableExpression = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParamExpression,
                SearchParamTableExpressionKind.Normal);

            var rootExpression = SqlRootExpression.WithSearchParamTableExpressions(tableExpression);

            var result = DateTimeTableExpressionCombiner.Instance.VisitSqlRoot(rootExpression, null);

            Assert.Same(rootExpression, result);
        }

        [Fact]
        public void GivenTwoDateTimeExpressionsWithGreaterThanOrEqualAndLessThanOrEqual_WhenVisitSqlRoot_ThenCombinesExpressions()
        {
            var greaterExpression = Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, null, TestStartDate);
            var lessExpression = Expression.LessThanOrEqual(FieldName.DateTimeStart, null, TestEndDate);

            var searchParam1 = new SearchParameterExpression(DateSearchParam, greaterExpression);
            var searchParam2 = new SearchParameterExpression(DateSearchParam, lessExpression);

            var tableExpression1 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam1,
                SearchParamTableExpressionKind.Normal);

            var tableExpression2 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam2,
                SearchParamTableExpressionKind.Normal);

            var rootExpression = new SqlRootExpression(
                new[] { tableExpression1, tableExpression2 },
                Array.Empty<SearchParameterExpressionBase>());

            var result = (SqlRootExpression)DateTimeTableExpressionCombiner.Instance.VisitSqlRoot(rootExpression, null);

            Assert.Single(result.SearchParamTableExpressions);
            var combinedExpression = result.SearchParamTableExpressions[0];
            Assert.IsType<SearchParameterExpression>(combinedExpression.Predicate);
            var searchParamExpression = (SearchParameterExpression)combinedExpression.Predicate;
            Assert.IsType<MultiaryExpression>(searchParamExpression.Expression);
            var multiaryExpression = (MultiaryExpression)searchParamExpression.Expression;
            Assert.Equal(MultiaryOperator.And, multiaryExpression.MultiaryOperation);
            Assert.Equal(2, multiaryExpression.Expressions.Count);
        }

        [Fact]
        public void GivenTwoDateTimeExpressionsWithGreaterThanAndLessThan_WhenVisitSqlRoot_ThenCombinesExpressions()
        {
            var greaterExpression = Expression.GreaterThan(FieldName.DateTimeEnd, null, TestStartDate);
            var lessExpression = Expression.LessThan(FieldName.DateTimeStart, null, TestEndDate);

            var searchParam1 = new SearchParameterExpression(DateSearchParam, greaterExpression);
            var searchParam2 = new SearchParameterExpression(DateSearchParam, lessExpression);

            var tableExpression1 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam1,
                SearchParamTableExpressionKind.Normal);

            var tableExpression2 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam2,
                SearchParamTableExpressionKind.Normal);

            var rootExpression = new SqlRootExpression(
                new[] { tableExpression1, tableExpression2 },
                Array.Empty<SearchParameterExpressionBase>());

            var result = (SqlRootExpression)DateTimeTableExpressionCombiner.Instance.VisitSqlRoot(rootExpression, null);

            Assert.Single(result.SearchParamTableExpressions);
        }

        [Fact]
        public void GivenTwoDateTimeExpressionsWithGreaterThanOrEqualAndLessThan_WhenVisitSqlRoot_ThenCombinesExpressions()
        {
            var greaterExpression = Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, null, TestStartDate);
            var lessExpression = Expression.LessThan(FieldName.DateTimeStart, null, TestEndDate);

            var searchParam1 = new SearchParameterExpression(DateSearchParam, greaterExpression);
            var searchParam2 = new SearchParameterExpression(DateSearchParam, lessExpression);

            var tableExpression1 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam1,
                SearchParamTableExpressionKind.Normal);

            var tableExpression2 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam2,
                SearchParamTableExpressionKind.Normal);

            var rootExpression = new SqlRootExpression(
                new[] { tableExpression1, tableExpression2 },
                Array.Empty<SearchParameterExpressionBase>());

            var result = (SqlRootExpression)DateTimeTableExpressionCombiner.Instance.VisitSqlRoot(rootExpression, null);

            Assert.Single(result.SearchParamTableExpressions);
        }

        [Fact]
        public void GivenTwoDateTimeExpressionsWithGreaterThanAndLessThanOrEqual_WhenVisitSqlRoot_ThenCombinesExpressions()
        {
            var greaterExpression = Expression.GreaterThan(FieldName.DateTimeEnd, null, TestStartDate);
            var lessExpression = Expression.LessThanOrEqual(FieldName.DateTimeStart, null, TestEndDate);

            var searchParam1 = new SearchParameterExpression(DateSearchParam, greaterExpression);
            var searchParam2 = new SearchParameterExpression(DateSearchParam, lessExpression);

            var tableExpression1 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam1,
                SearchParamTableExpressionKind.Normal);

            var tableExpression2 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam2,
                SearchParamTableExpressionKind.Normal);

            var rootExpression = new SqlRootExpression(
                new[] { tableExpression1, tableExpression2 },
                Array.Empty<SearchParameterExpressionBase>());

            var result = (SqlRootExpression)DateTimeTableExpressionCombiner.Instance.VisitSqlRoot(rootExpression, null);

            Assert.Single(result.SearchParamTableExpressions);
        }

        [Fact]
        public void GivenTwoNonDateTimeExpressions_WhenVisitSqlRoot_ThenDoesNotCombine()
        {
            var tokenExpression1 = Expression.Equals(FieldName.TokenCode, null, "code1");
            var tokenExpression2 = Expression.Equals(FieldName.TokenCode, null, "code2");

            var searchParam1 = new SearchParameterExpression(TokenSearchParam, tokenExpression1);
            var searchParam2 = new SearchParameterExpression(TokenSearchParam, tokenExpression2);

            var tableExpression1 = new SearchParamTableExpression(
                TokenQueryGenerator.Instance,
                searchParam1,
                SearchParamTableExpressionKind.Normal);

            var tableExpression2 = new SearchParamTableExpression(
                TokenQueryGenerator.Instance,
                searchParam2,
                SearchParamTableExpressionKind.Normal);

            var rootExpression = new SqlRootExpression(
                new[] { tableExpression1, tableExpression2 },
                Array.Empty<SearchParameterExpressionBase>());

            var result = (SqlRootExpression)DateTimeTableExpressionCombiner.Instance.VisitSqlRoot(rootExpression, null);

            Assert.Equal(2, result.SearchParamTableExpressions.Count);
            Assert.Same(tableExpression1, result.SearchParamTableExpressions[0]);
            Assert.Same(tableExpression2, result.SearchParamTableExpressions[1]);
        }

        [Fact]
        public void GivenThreeDateTimeExpressions_WhenVisitSqlRoot_ThenDoesNotCombine()
        {
            var expression1 = Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, null, TestStartDate);
            var expression2 = Expression.LessThanOrEqual(FieldName.DateTimeStart, null, TestEndDate);
            var expression3 = Expression.GreaterThan(FieldName.DateTimeEnd, null, TestStartDate);

            var searchParam1 = new SearchParameterExpression(DateSearchParam, expression1);
            var searchParam2 = new SearchParameterExpression(DateSearchParam, expression2);
            var searchParam3 = new SearchParameterExpression(DateSearchParam, expression3);

            var tableExpression1 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam1,
                SearchParamTableExpressionKind.Normal);

            var tableExpression2 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam2,
                SearchParamTableExpressionKind.Normal);

            var tableExpression3 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam3,
                SearchParamTableExpressionKind.Normal);

            var rootExpression = new SqlRootExpression(
                new[] { tableExpression1, tableExpression2, tableExpression3 },
                Array.Empty<SearchParameterExpressionBase>());

            var result = (SqlRootExpression)DateTimeTableExpressionCombiner.Instance.VisitSqlRoot(rootExpression, null);

            Assert.Equal(3, result.SearchParamTableExpressions.Count);
        }

        [Fact]
        public void GivenOneDateTimeExpression_WhenVisitSqlRoot_ThenDoesNotCombine()
        {
            var expression = Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, null, TestStartDate);
            var searchParam = new SearchParameterExpression(DateSearchParam, expression);

            var tableExpression = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam,
                SearchParamTableExpressionKind.Normal);

            var rootExpression = SqlRootExpression.WithSearchParamTableExpressions(tableExpression);

            var result = (SqlRootExpression)DateTimeTableExpressionCombiner.Instance.VisitSqlRoot(rootExpression, null);

            Assert.Same(rootExpression, result);
        }

        [Fact]
        public void GivenTwoDateTimeExpressionsWithWrongOperators_WhenVisitSqlRoot_ThenDoesNotCombine()
        {
            // Both are GreaterThan - missing LessThan
            var expression1 = Expression.GreaterThan(FieldName.DateTimeEnd, null, TestStartDate);
            var expression2 = Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, null, TestStartDate);

            var searchParam1 = new SearchParameterExpression(DateSearchParam, expression1);
            var searchParam2 = new SearchParameterExpression(DateSearchParam, expression2);

            var tableExpression1 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam1,
                SearchParamTableExpressionKind.Normal);

            var tableExpression2 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam2,
                SearchParamTableExpressionKind.Normal);

            var rootExpression = new SqlRootExpression(
                new[] { tableExpression1, tableExpression2 },
                Array.Empty<SearchParameterExpressionBase>());

            var result = (SqlRootExpression)DateTimeTableExpressionCombiner.Instance.VisitSqlRoot(rootExpression, null);

            Assert.Equal(2, result.SearchParamTableExpressions.Count);
        }

        [Fact]
        public void GivenTwoDateTimeExpressionsWithWrongFields_WhenVisitSqlRoot_ThenDoesNotCombine()
        {
            // Both use DateTimeStart instead of End for GreaterThan
            var expression1 = Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, TestStartDate);
            var expression2 = Expression.LessThanOrEqual(FieldName.DateTimeStart, null, TestEndDate);

            var searchParam1 = new SearchParameterExpression(DateSearchParam, expression1);
            var searchParam2 = new SearchParameterExpression(DateSearchParam, expression2);

            var tableExpression1 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam1,
                SearchParamTableExpressionKind.Normal);

            var tableExpression2 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam2,
                SearchParamTableExpressionKind.Normal);

            var rootExpression = new SqlRootExpression(
                new[] { tableExpression1, tableExpression2 },
                Array.Empty<SearchParameterExpressionBase>());

            var result = (SqlRootExpression)DateTimeTableExpressionCombiner.Instance.VisitSqlRoot(rootExpression, null);

            Assert.Equal(2, result.SearchParamTableExpressions.Count);
        }

        [Fact]
        public void GivenMultipleSearchParameters_WhenVisitSqlRoot_ThenOnlyCombinesMatchingPairs()
        {
            var dateSearchParam1 = new SearchParameterInfo(
                name: "issued",
                code: "issued",
                searchParamType: SearchParamType.Date,
                url: new Uri("http://hl7.org/fhir/SearchParameter/Observation-issued"));

            var dateSearchParam2 = new SearchParameterInfo(
                name: "date",
                code: "date",
                searchParamType: SearchParamType.Date,
                url: new Uri("http://hl7.org/fhir/SearchParameter/Observation-date"));

            // First pair - should be combined
            var expression1 = Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, null, TestStartDate);
            var expression2 = Expression.LessThanOrEqual(FieldName.DateTimeStart, null, TestEndDate);

            // Second pair - different search parameter, should not be combined with first
            var expression3 = Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, null, TestStartDate);
            var expression4 = Expression.LessThanOrEqual(FieldName.DateTimeStart, null, TestEndDate);

            var searchParam1 = new SearchParameterExpression(dateSearchParam1, expression1);
            var searchParam2 = new SearchParameterExpression(dateSearchParam1, expression2);
            var searchParam3 = new SearchParameterExpression(dateSearchParam2, expression3);
            var searchParam4 = new SearchParameterExpression(dateSearchParam2, expression4);

            var tableExpression1 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam1,
                SearchParamTableExpressionKind.Normal);

            var tableExpression2 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam2,
                SearchParamTableExpressionKind.Normal);

            var tableExpression3 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam3,
                SearchParamTableExpressionKind.Normal);

            var tableExpression4 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam4,
                SearchParamTableExpressionKind.Normal);

            var rootExpression = new SqlRootExpression(
                new[] { tableExpression1, tableExpression2, tableExpression3, tableExpression4 },
                Array.Empty<SearchParameterExpressionBase>());

            var result = (SqlRootExpression)DateTimeTableExpressionCombiner.Instance.VisitSqlRoot(rootExpression, null);

            // Both pairs should be combined, resulting in 2 expressions
            Assert.Equal(2, result.SearchParamTableExpressions.Count);
        }

        [Fact]
        public void GivenMixedDateTimeAndTokenExpressions_WhenVisitSqlRoot_ThenOnlyCombinesDateTime()
        {
            var dateExpression1 = Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, null, TestStartDate);
            var dateExpression2 = Expression.LessThanOrEqual(FieldName.DateTimeStart, null, TestEndDate);
            var tokenExpression = Expression.Equals(FieldName.TokenCode, null, "code1");

            var searchParam1 = new SearchParameterExpression(DateSearchParam, dateExpression1);
            var searchParam2 = new SearchParameterExpression(DateSearchParam, dateExpression2);
            var searchParam3 = new SearchParameterExpression(TokenSearchParam, tokenExpression);

            var tableExpression1 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam1,
                SearchParamTableExpressionKind.Normal);

            var tableExpression2 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam2,
                SearchParamTableExpressionKind.Normal);

            var tableExpression3 = new SearchParamTableExpression(
                TokenQueryGenerator.Instance,
                searchParam3,
                SearchParamTableExpressionKind.Normal);

            var rootExpression = new SqlRootExpression(
                new[] { tableExpression1, tableExpression2, tableExpression3 },
                Array.Empty<SearchParameterExpressionBase>());

            var result = (SqlRootExpression)DateTimeTableExpressionCombiner.Instance.VisitSqlRoot(rootExpression, null);

            Assert.Equal(2, result.SearchParamTableExpressions.Count);

            // Verify token expression is preserved (find it in the result)
            var tokenTableExpression = result.SearchParamTableExpressions
                .FirstOrDefault(e => e.QueryGenerator == TokenQueryGenerator.Instance);
            Assert.NotNull(tokenTableExpression);
        }

        [Fact]
        public void GivenCombinedExpressions_WhenVisitSqlRoot_ThenCreatesCorrectMultiaryExpression()
        {
            var greaterExpression = Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, null, TestStartDate);
            var lessExpression = Expression.LessThanOrEqual(FieldName.DateTimeStart, null, TestEndDate);

            var searchParam1 = new SearchParameterExpression(DateSearchParam, greaterExpression);
            var searchParam2 = new SearchParameterExpression(DateSearchParam, lessExpression);

            var tableExpression1 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam1,
                SearchParamTableExpressionKind.Normal);

            var tableExpression2 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam2,
                SearchParamTableExpressionKind.Normal);

            var rootExpression = new SqlRootExpression(
                new[] { tableExpression1, tableExpression2 },
                Array.Empty<SearchParameterExpressionBase>());

            var result = (SqlRootExpression)DateTimeTableExpressionCombiner.Instance.VisitSqlRoot(rootExpression, null);

            Assert.Single(result.SearchParamTableExpressions);

            var combinedExpression = result.SearchParamTableExpressions[0];
            var searchParamExpression = (SearchParameterExpression)combinedExpression.Predicate;
            Assert.Same(DateSearchParam, searchParamExpression.Parameter);

            var multiaryExpression = (MultiaryExpression)searchParamExpression.Expression;
            Assert.Equal(MultiaryOperator.And, multiaryExpression.MultiaryOperation);
            Assert.Equal(2, multiaryExpression.Expressions.Count);
            Assert.Contains(greaterExpression, multiaryExpression.Expressions);
            Assert.Contains(lessExpression, multiaryExpression.Expressions);
        }

        [Fact]
        public void GivenCombinedExpressions_WhenVisitSqlRoot_ThenPreservesOtherExpressions()
        {
            var dateExpression1 = Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, null, TestStartDate);
            var dateExpression2 = Expression.LessThanOrEqual(FieldName.DateTimeStart, null, TestEndDate);
            var tokenExpression1 = Expression.Equals(FieldName.TokenCode, null, "code1");
            var tokenExpression2 = Expression.Equals(FieldName.TokenCode, null, "code2");

            var dateSearchParam1 = new SearchParameterExpression(DateSearchParam, dateExpression1);
            var dateSearchParam2 = new SearchParameterExpression(DateSearchParam, dateExpression2);
            var tokenSearchParam1 = new SearchParameterExpression(TokenSearchParam, tokenExpression1);
            var tokenSearchParam2 = new SearchParameterExpression(TokenSearchParam, tokenExpression2);

            var tableExpression1 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                dateSearchParam1,
                SearchParamTableExpressionKind.Normal);

            var tableExpression2 = new SearchParamTableExpression(
                TokenQueryGenerator.Instance,
                tokenSearchParam1,
                SearchParamTableExpressionKind.Normal);

            var tableExpression3 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                dateSearchParam2,
                SearchParamTableExpressionKind.Normal);

            var tableExpression4 = new SearchParamTableExpression(
                TokenQueryGenerator.Instance,
                tokenSearchParam2,
                SearchParamTableExpressionKind.Normal);

            var rootExpression = new SqlRootExpression(
                new[] { tableExpression1, tableExpression2, tableExpression3, tableExpression4 },
                Array.Empty<SearchParameterExpressionBase>());

            var result = (SqlRootExpression)DateTimeTableExpressionCombiner.Instance.VisitSqlRoot(rootExpression, null);

            Assert.Equal(3, result.SearchParamTableExpressions.Count);

            // Verify token expressions are preserved
            var tokenExpressions = result.SearchParamTableExpressions
                .Where(e => e.QueryGenerator == TokenQueryGenerator.Instance)
                .ToList();
            Assert.Equal(2, tokenExpressions.Count);
        }

        [Fact]
        public void GivenCombinedExpressions_WhenVisitSqlRoot_ThenUsesCorrectQueryGenerator()
        {
            var greaterExpression = Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, null, TestStartDate);
            var lessExpression = Expression.LessThanOrEqual(FieldName.DateTimeStart, null, TestEndDate);

            var searchParam1 = new SearchParameterExpression(DateSearchParam, greaterExpression);
            var searchParam2 = new SearchParameterExpression(DateSearchParam, lessExpression);

            var tableExpression1 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam1,
                SearchParamTableExpressionKind.Normal);

            var tableExpression2 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam2,
                SearchParamTableExpressionKind.Normal);

            var rootExpression = new SqlRootExpression(
                new[] { tableExpression1, tableExpression2 },
                Array.Empty<SearchParameterExpressionBase>());

            var result = (SqlRootExpression)DateTimeTableExpressionCombiner.Instance.VisitSqlRoot(rootExpression, null);

            Assert.Single(result.SearchParamTableExpressions);
            var combinedExpression = result.SearchParamTableExpressions[0];
            Assert.Same(DateTimeQueryGenerator.Instance, combinedExpression.QueryGenerator);
            Assert.Equal(SearchParamTableExpressionKind.Normal, combinedExpression.Kind);
        }

        [Fact]
        public void GivenResourceTableExpressions_WhenVisitSqlRoot_ThenPreservesResourceTableExpressions()
        {
            var dateExpression1 = Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, null, TestStartDate);
            var dateExpression2 = Expression.LessThanOrEqual(FieldName.DateTimeStart, null, TestEndDate);

            var searchParam1 = new SearchParameterExpression(DateSearchParam, dateExpression1);
            var searchParam2 = new SearchParameterExpression(DateSearchParam, dateExpression2);

            var tableExpression1 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam1,
                SearchParamTableExpressionKind.Normal);

            var tableExpression2 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam2,
                SearchParamTableExpressionKind.Normal);

            var resourceExpression = new SearchParameterExpression(
                TokenSearchParam,
                Expression.Equals(FieldName.TokenCode, null, "active"));

            var rootExpression = new SqlRootExpression(
                new[] { tableExpression1, tableExpression2 },
                new[] { resourceExpression });

            var result = (SqlRootExpression)DateTimeTableExpressionCombiner.Instance.VisitSqlRoot(rootExpression, null);

            Assert.Single(result.SearchParamTableExpressions);
            Assert.Single(result.ResourceTableExpressions);
            Assert.Same(resourceExpression, result.ResourceTableExpressions[0]);
        }

        [Fact]
        public void GivenNullPredicate_WhenVisitSqlRoot_ThenDoesNotCombine()
        {
            var tableExpression1 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                null,
                SearchParamTableExpressionKind.Normal);

            var greaterExpression = Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, null, TestStartDate);
            var searchParam = new SearchParameterExpression(DateSearchParam, greaterExpression);

            var tableExpression2 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam,
                SearchParamTableExpressionKind.Normal);

            var rootExpression = new SqlRootExpression(
                new[] { tableExpression1, tableExpression2 },
                Array.Empty<SearchParameterExpressionBase>());

            var result = (SqlRootExpression)DateTimeTableExpressionCombiner.Instance.VisitSqlRoot(rootExpression, null);

            Assert.Equal(2, result.SearchParamTableExpressions.Count);
        }

        [Fact]
        public void GivenNonBinaryExpression_WhenVisitSqlRoot_ThenDoesNotCombine()
        {
            var multiaryExpression = Expression.And(
                Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, null, TestStartDate),
                Expression.LessThanOrEqual(FieldName.DateTimeStart, null, TestEndDate));

            var searchParam1 = new SearchParameterExpression(DateSearchParam, multiaryExpression);
            var searchParam2 = new SearchParameterExpression(DateSearchParam, multiaryExpression);

            var tableExpression1 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam1,
                SearchParamTableExpressionKind.Normal);

            var tableExpression2 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam2,
                SearchParamTableExpressionKind.Normal);

            var rootExpression = new SqlRootExpression(
                new[] { tableExpression1, tableExpression2 },
                Array.Empty<SearchParameterExpressionBase>());

            var result = (SqlRootExpression)DateTimeTableExpressionCombiner.Instance.VisitSqlRoot(rootExpression, null);

            Assert.Equal(2, result.SearchParamTableExpressions.Count);
        }

        [Fact]
        public void GivenExpressionsInReverseOrder_WhenVisitSqlRoot_ThenStillCombines()
        {
            // LessThan first, then GreaterThan
            var lessExpression = Expression.LessThanOrEqual(FieldName.DateTimeStart, null, TestEndDate);
            var greaterExpression = Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, null, TestStartDate);

            var searchParam1 = new SearchParameterExpression(DateSearchParam, lessExpression);
            var searchParam2 = new SearchParameterExpression(DateSearchParam, greaterExpression);

            var tableExpression1 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam1,
                SearchParamTableExpressionKind.Normal);

            var tableExpression2 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam2,
                SearchParamTableExpressionKind.Normal);

            var rootExpression = new SqlRootExpression(
                new[] { tableExpression1, tableExpression2 },
                Array.Empty<SearchParameterExpressionBase>());

            var result = (SqlRootExpression)DateTimeTableExpressionCombiner.Instance.VisitSqlRoot(rootExpression, null);

            Assert.Single(result.SearchParamTableExpressions);
        }

        [Fact]
        public void GivenDateTimeExpressionsWithConcatenationKind_WhenVisitSqlRoot_ThenCombinesExpressionsAndPreservesKind()
        {
            var greaterExpression = Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, null, TestStartDate);
            var lessExpression = Expression.LessThanOrEqual(FieldName.DateTimeStart, null, TestEndDate);

            var searchParam1 = new SearchParameterExpression(DateSearchParam, greaterExpression);
            var searchParam2 = new SearchParameterExpression(DateSearchParam, lessExpression);

            var tableExpression1 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam1,
                SearchParamTableExpressionKind.Concatenation);

            var tableExpression2 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam2,
                SearchParamTableExpressionKind.Concatenation);

            var rootExpression = new SqlRootExpression(
                new[] { tableExpression1, tableExpression2 },
                Array.Empty<SearchParameterExpressionBase>());

            var result = (SqlRootExpression)DateTimeTableExpressionCombiner.Instance.VisitSqlRoot(rootExpression, null);

            Assert.Single(result.SearchParamTableExpressions);
            Assert.Equal(SearchParamTableExpressionKind.Normal, result.SearchParamTableExpressions[0].Kind);
        }

        [Fact]
        public void GivenDateTimeExpressionsWithNotExistsKind_WhenVisitSqlRoot_ThenCombinesExpressionsAndUsesNormalKind()
        {
            var greaterExpression = Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, null, TestStartDate);
            var lessExpression = Expression.LessThanOrEqual(FieldName.DateTimeStart, null, TestEndDate);

            var searchParam1 = new SearchParameterExpression(DateSearchParam, greaterExpression);
            var searchParam2 = new SearchParameterExpression(DateSearchParam, lessExpression);

            var tableExpression1 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam1,
                SearchParamTableExpressionKind.NotExists);

            var tableExpression2 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam2,
                SearchParamTableExpressionKind.NotExists);

            var rootExpression = new SqlRootExpression(
                new[] { tableExpression1, tableExpression2 },
                Array.Empty<SearchParameterExpressionBase>());

            var result = (SqlRootExpression)DateTimeTableExpressionCombiner.Instance.VisitSqlRoot(rootExpression, null);

            Assert.Single(result.SearchParamTableExpressions);
            Assert.Equal(SearchParamTableExpressionKind.Normal, result.SearchParamTableExpressions[0].Kind);
        }

        [Fact]
        public void GivenDateTimeExpressionsWithChainLevel_WhenVisitSqlRoot_ThenCombinesAndUsesZeroChainLevel()
        {
            var greaterExpression = Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, null, TestStartDate);
            var lessExpression = Expression.LessThanOrEqual(FieldName.DateTimeStart, null, TestEndDate);

            var searchParam1 = new SearchParameterExpression(DateSearchParam, greaterExpression);
            var searchParam2 = new SearchParameterExpression(DateSearchParam, lessExpression);

            var tableExpression1 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam1,
                SearchParamTableExpressionKind.Normal,
                chainLevel: 1);

            var tableExpression2 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam2,
                SearchParamTableExpressionKind.Normal,
                chainLevel: 1);

            var rootExpression = new SqlRootExpression(
                new[] { tableExpression1, tableExpression2 },
                Array.Empty<SearchParameterExpressionBase>());

            var result = (SqlRootExpression)DateTimeTableExpressionCombiner.Instance.VisitSqlRoot(rootExpression, null);

            Assert.Single(result.SearchParamTableExpressions);
            Assert.Equal(0, result.SearchParamTableExpressions[0].ChainLevel);
        }

        [Fact]
        public void GivenDateTimeExpressionsWithDifferentChainLevels_WhenVisitSqlRoot_ThenCombinesRegardlessOfChainLevel()
        {
            // Note: The current implementation groups by SearchParameterInfo only,
            // so expressions with different chainLevels are still combined.
            var greaterExpression = Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, null, TestStartDate);
            var lessExpression = Expression.LessThanOrEqual(FieldName.DateTimeStart, null, TestEndDate);

            var searchParam1 = new SearchParameterExpression(DateSearchParam, greaterExpression);
            var searchParam2 = new SearchParameterExpression(DateSearchParam, lessExpression);

            var tableExpression1 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam1,
                SearchParamTableExpressionKind.Normal,
                chainLevel: 0);

            var tableExpression2 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam2,
                SearchParamTableExpressionKind.Normal,
                chainLevel: 1);

            var rootExpression = new SqlRootExpression(
                new[] { tableExpression1, tableExpression2 },
                Array.Empty<SearchParameterExpressionBase>());

            var result = (SqlRootExpression)DateTimeTableExpressionCombiner.Instance.VisitSqlRoot(rootExpression, null);

            // Implementation combines expressions even with different chainLevels
            // since grouping is only by SearchParameterInfo
            Assert.Single(result.SearchParamTableExpressions);
            Assert.Equal(0, result.SearchParamTableExpressions[0].ChainLevel);
        }

        [Fact]
        public void GivenFourDateTimeExpressionsForTwoParameters_WhenVisitSqlRoot_ThenCombinesBothPairs()
        {
            var dateSearchParam1 = new SearchParameterInfo(
                name: "issued",
                code: "issued",
                searchParamType: SearchParamType.Date,
                url: new Uri("http://hl7.org/fhir/SearchParameter/Observation-issued"));

            var dateSearchParam2 = new SearchParameterInfo(
                name: "date",
                code: "date",
                searchParamType: SearchParamType.Date,
                url: new Uri("http://hl7.org/fhir/SearchParameter/Observation-date"));

            var expression1 = Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, null, TestStartDate);
            var expression2 = Expression.LessThanOrEqual(FieldName.DateTimeStart, null, TestEndDate);
            var expression3 = Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, null, TestStartDate);
            var expression4 = Expression.LessThanOrEqual(FieldName.DateTimeStart, null, TestEndDate);

            var searchParam1 = new SearchParameterExpression(dateSearchParam1, expression1);
            var searchParam2 = new SearchParameterExpression(dateSearchParam1, expression2);
            var searchParam3 = new SearchParameterExpression(dateSearchParam2, expression3);
            var searchParam4 = new SearchParameterExpression(dateSearchParam2, expression4);

            var tableExpression1 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam1,
                SearchParamTableExpressionKind.Normal);

            var tableExpression2 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam2,
                SearchParamTableExpressionKind.Normal);

            var tableExpression3 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam3,
                SearchParamTableExpressionKind.Normal);

            var tableExpression4 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam4,
                SearchParamTableExpressionKind.Normal);

            var rootExpression = new SqlRootExpression(
                new[] { tableExpression1, tableExpression2, tableExpression3, tableExpression4 },
                Array.Empty<SearchParameterExpressionBase>());

            var result = (SqlRootExpression)DateTimeTableExpressionCombiner.Instance.VisitSqlRoot(rootExpression, null);

            Assert.Equal(2, result.SearchParamTableExpressions.Count);

            foreach (var tableExpression in result.SearchParamTableExpressions)
            {
                var searchParamExpression = (SearchParameterExpression)tableExpression.Predicate;
                var multiaryExpression = (MultiaryExpression)searchParamExpression.Expression;
                Assert.Equal(2, multiaryExpression.Expressions.Count);
            }
        }

        [Fact]
        public void GivenDateTimeExpressionsWithComponentIndex_WhenVisitSqlRoot_ThenCombinesExpressionsCorrectly()
        {
            var greaterExpression = Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, 0, TestStartDate);
            var lessExpression = Expression.LessThanOrEqual(FieldName.DateTimeStart, 0, TestEndDate);

            var searchParam1 = new SearchParameterExpression(DateSearchParam, greaterExpression);
            var searchParam2 = new SearchParameterExpression(DateSearchParam, lessExpression);

            var tableExpression1 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam1,
                SearchParamTableExpressionKind.Normal);

            var tableExpression2 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam2,
                SearchParamTableExpressionKind.Normal);

            var rootExpression = new SqlRootExpression(
                new[] { tableExpression1, tableExpression2 },
                Array.Empty<SearchParameterExpressionBase>());

            var result = (SqlRootExpression)DateTimeTableExpressionCombiner.Instance.VisitSqlRoot(rootExpression, null);

            Assert.Single(result.SearchParamTableExpressions);

            var combinedExpression = result.SearchParamTableExpressions[0];
            var searchParamExpression = (SearchParameterExpression)combinedExpression.Predicate;
            var multiaryExpression = (MultiaryExpression)searchParamExpression.Expression;

            Assert.Equal(2, multiaryExpression.Expressions.Count);
            Assert.Contains(greaterExpression, multiaryExpression.Expressions);
            Assert.Contains(lessExpression, multiaryExpression.Expressions);
        }

        [Fact]
        public void GivenCombinedExpression_WhenVisitSqlRoot_ThenExpressionsInMultiaryAreExactlyOriginalExpressions()
        {
            var greaterExpression = Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, null, TestStartDate);
            var lessExpression = Expression.LessThanOrEqual(FieldName.DateTimeStart, null, TestEndDate);

            var searchParam1 = new SearchParameterExpression(DateSearchParam, greaterExpression);
            var searchParam2 = new SearchParameterExpression(DateSearchParam, lessExpression);

            var tableExpression1 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam1,
                SearchParamTableExpressionKind.Normal);

            var tableExpression2 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam2,
                SearchParamTableExpressionKind.Normal);

            var rootExpression = new SqlRootExpression(
                new[] { tableExpression1, tableExpression2 },
                Array.Empty<SearchParameterExpressionBase>());

            var result = (SqlRootExpression)DateTimeTableExpressionCombiner.Instance.VisitSqlRoot(rootExpression, null);

            var combinedExpression = result.SearchParamTableExpressions[0];
            var searchParamExpression = (SearchParameterExpression)combinedExpression.Predicate;
            var multiaryExpression = (MultiaryExpression)searchParamExpression.Expression;

            Assert.Equal(2, multiaryExpression.Expressions.Count);
            Assert.True(
                (ReferenceEquals(multiaryExpression.Expressions[0], greaterExpression) && ReferenceEquals(multiaryExpression.Expressions[1], lessExpression)) ||
                (ReferenceEquals(multiaryExpression.Expressions[0], lessExpression) && ReferenceEquals(multiaryExpression.Expressions[1], greaterExpression)),
                "Combined multiary expression should contain exact references to original expressions");
        }

        [Fact]
        public void GivenCombinedExpression_WhenVisitSqlRoot_ThenQueryGeneratorIsPreserved()
        {
            var greaterExpression = Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, null, TestStartDate);
            var lessExpression = Expression.LessThanOrEqual(FieldName.DateTimeStart, null, TestEndDate);

            var searchParam1 = new SearchParameterExpression(DateSearchParam, greaterExpression);
            var searchParam2 = new SearchParameterExpression(DateSearchParam, lessExpression);

            var tableExpression1 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam1,
                SearchParamTableExpressionKind.Normal);

            var tableExpression2 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam2,
                SearchParamTableExpressionKind.Normal);

            var rootExpression = new SqlRootExpression(
                new[] { tableExpression1, tableExpression2 },
                Array.Empty<SearchParameterExpressionBase>());

            var result = (SqlRootExpression)DateTimeTableExpressionCombiner.Instance.VisitSqlRoot(rootExpression, null);

            var combinedExpression = result.SearchParamTableExpressions[0];
            Assert.Same(DateTimeQueryGenerator.Instance, combinedExpression.QueryGenerator);
        }

        [Fact]
        public void GivenMultipleTokenExpressionsWithSameParameter_WhenVisitSqlRoot_ThenDoesNotCombine()
        {
            var tokenExpression1 = Expression.Equals(FieldName.TokenCode, null, "code1");
            var tokenExpression2 = Expression.Equals(FieldName.TokenCode, null, "code2");

            var searchParam1 = new SearchParameterExpression(TokenSearchParam, tokenExpression1);
            var searchParam2 = new SearchParameterExpression(TokenSearchParam, tokenExpression2);

            var tableExpression1 = new SearchParamTableExpression(
                TokenQueryGenerator.Instance,
                searchParam1,
                SearchParamTableExpressionKind.Normal);

            var tableExpression2 = new SearchParamTableExpression(
                TokenQueryGenerator.Instance,
                searchParam2,
                SearchParamTableExpressionKind.Normal);

            var rootExpression = new SqlRootExpression(
                new[] { tableExpression1, tableExpression2 },
                Array.Empty<SearchParameterExpressionBase>());

            var result = (SqlRootExpression)DateTimeTableExpressionCombiner.Instance.VisitSqlRoot(rootExpression, null);

            Assert.Equal(2, result.SearchParamTableExpressions.Count);
            Assert.Same(tableExpression1, result.SearchParamTableExpressions[0]);
            Assert.Same(tableExpression2, result.SearchParamTableExpressions[1]);
        }

        [Fact]
        public void GivenBothGreaterThanOperators_WhenVisitSqlRoot_ThenDoesNotCombine()
        {
            var expression1 = Expression.GreaterThan(FieldName.DateTimeEnd, null, TestStartDate);
            var expression2 = Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, null, TestEndDate);

            var searchParam1 = new SearchParameterExpression(DateSearchParam, expression1);
            var searchParam2 = new SearchParameterExpression(DateSearchParam, expression2);

            var tableExpression1 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam1,
                SearchParamTableExpressionKind.Normal);

            var tableExpression2 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam2,
                SearchParamTableExpressionKind.Normal);

            var rootExpression = new SqlRootExpression(
                new[] { tableExpression1, tableExpression2 },
                Array.Empty<SearchParameterExpressionBase>());

            var result = (SqlRootExpression)DateTimeTableExpressionCombiner.Instance.VisitSqlRoot(rootExpression, null);

            Assert.Equal(2, result.SearchParamTableExpressions.Count);
        }

        [Fact]
        public void GivenBothLessThanOperators_WhenVisitSqlRoot_ThenDoesNotCombine()
        {
            var expression1 = Expression.LessThan(FieldName.DateTimeStart, null, TestStartDate);
            var expression2 = Expression.LessThanOrEqual(FieldName.DateTimeStart, null, TestEndDate);

            var searchParam1 = new SearchParameterExpression(DateSearchParam, expression1);
            var searchParam2 = new SearchParameterExpression(DateSearchParam, expression2);

            var tableExpression1 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam1,
                SearchParamTableExpressionKind.Normal);

            var tableExpression2 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam2,
                SearchParamTableExpressionKind.Normal);

            var rootExpression = new SqlRootExpression(
                new[] { tableExpression1, tableExpression2 },
                Array.Empty<SearchParameterExpressionBase>());

            var result = (SqlRootExpression)DateTimeTableExpressionCombiner.Instance.VisitSqlRoot(rootExpression, null);

            Assert.Equal(2, result.SearchParamTableExpressions.Count);
        }

        [Fact]
        public void GivenDateTimeExpressionsWithSort_WhenVisitSqlRoot_ThenCombinesExpressionsAndUsesNormalKind()
        {
            var greaterExpression = Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, null, TestStartDate);
            var lessExpression = Expression.LessThanOrEqual(FieldName.DateTimeStart, null, TestEndDate);

            var searchParam1 = new SearchParameterExpression(DateSearchParam, greaterExpression);
            var searchParam2 = new SearchParameterExpression(DateSearchParam, lessExpression);

            var tableExpression1 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam1,
                SearchParamTableExpressionKind.Sort);

            var tableExpression2 = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                searchParam2,
                SearchParamTableExpressionKind.Sort);

            var rootExpression = new SqlRootExpression(
                new[] { tableExpression1, tableExpression2 },
                Array.Empty<SearchParameterExpressionBase>());

            var result = (SqlRootExpression)DateTimeTableExpressionCombiner.Instance.VisitSqlRoot(rootExpression, null);

            Assert.Single(result.SearchParamTableExpressions);
            Assert.Equal(SearchParamTableExpressionKind.Normal, result.SearchParamTableExpressions[0].Kind);
        }
    }
}
