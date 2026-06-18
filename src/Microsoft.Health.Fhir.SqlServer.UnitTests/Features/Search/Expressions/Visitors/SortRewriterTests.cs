// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions.Visitors
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SortRewriterTests
    {
        private static readonly SearchParameterInfo BirthdateParam = new SearchParameterInfo(
            "birthdate",
            "birthdate",
            SearchParamType.Date,
            new Uri("http://hl7.org/fhir/SearchParameter/individual-birthdate"),
            expression: "Patient.birthDate",
            baseResourceTypes: new[] { "Patient" });

        private static readonly SearchParameterInfo NameParam = new SearchParameterInfo(
            "name",
            "name",
            SearchParamType.String,
            new Uri("http://hl7.org/fhir/SearchParameter/individual-name"),
            expression: "Patient.name",
            baseResourceTypes: new[] { "Patient" });

        private static readonly DateTimeOffset StartOfDay = new DateTimeOffset(2016, 7, 6, 0, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset EndOfDay = new DateTimeOffset(2016, 7, 6, 23, 59, 59, TimeSpan.Zero).AddTicks(9999999);

        private readonly SortRewriter _rewriter;

        public SortRewriterTests()
        {
            var map = new SearchParameterToSearchValueTypeMap();
            var factory = new SearchParamTableExpressionQueryGeneratorFactory(map);
            _rewriter = new SortRewriter(factory);
        }

        private static SqlSearchOptions BuildSortOptions(SearchParameterInfo sortParam, SortOrder order = SortOrder.Ascending)
        {
            var inner = new SearchOptions
            {
                Sort = new List<(SearchParameterInfo, SortOrder)> { (sortParam, order) },
                UnsupportedSearchParams = new List<Tuple<string, string>>(),
                ResourceVersionTypes = ResourceVersionType.Latest,
            };
            return new SqlSearchOptions(inner);
        }

        /// <summary>
        /// Builds the bare UnionExpression that ScalarTemporalEqualityRewriter emits for a day-precision birthdate.
        /// Both branches are SearchParameterExpression(birthdate, ...) so the sort rewriter's VisitUnion
        /// must detect that ALL branches contain the sort parameter.
        /// </summary>
        private static UnionExpression BuildBirthdateDaySplitUnion()
        {
            var shortBranch = new SearchParameterExpression(
                BirthdateParam,
                Expression.And(
                    Expression.Equals(SqlFieldName.DateTimeIsLongerThanADay, null, false),
                    Expression.Equals(FieldName.DateTimeEnd, null, EndOfDay)));

            var longBranch = new SearchParameterExpression(
                BirthdateParam,
                Expression.And(
                    Expression.Equals(SqlFieldName.DateTimeIsLongerThanADay, null, true),
                    Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, StartOfDay),
                    Expression.LessThanOrEqual(FieldName.DateTimeEnd, null, EndOfDay)));

            return Expression.Union(UnionOperator.All, new Expression[] { shortBranch, longBranch });
        }

        [Fact]
        public void GivenUnionPredicateWhereAllBranchesMatchSortParam_WhenRewritten_ThenMatchFoundAndSortWithFilterEmitted()
        {
            // When every branch of a bare union references the sort parameter,
            // the rewriter must treat the predicate as "found" and emit SortWithFilter.
            var unionPredicate = BuildBirthdateDaySplitUnion();
            var tableExpr = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                unionPredicate,
                SearchParamTableExpressionKind.Normal);
            var root = SqlRootExpression.WithSearchParamTableExpressions(
                new List<SearchParamTableExpression> { tableExpr });
            var options = BuildSortOptions(BirthdateParam);

            var result = (SqlRootExpression)root.AcceptVisitor(_rewriter, options);

            Assert.True(options.IsSortWithFilter, "IsSortWithFilter should be set when the union covers the sort parameter on all branches.");
            Assert.Contains(result.SearchParamTableExpressions, e => e.Kind == SearchParamTableExpressionKind.SortWithFilter);
            Assert.DoesNotContain(result.SearchParamTableExpressions, e => e.Kind == SearchParamTableExpressionKind.Sort);
            Assert.DoesNotContain(result.SearchParamTableExpressions, e => e.Kind == SearchParamTableExpressionKind.NotExists);
        }

        [Fact]
        public void GivenUnionPredicateWhereNoBranchMatchesSortParam_WhenRewritten_ThenNoMatchAndSortEmitted()
        {
            // When no branch references the sort parameter, VisitUnion must NOT signal "found".
            // Using descending sort + no continuation token so VisitSqlRoot appends Sort (not NotExists).
            var nameBranch1 = new SearchParameterExpression(NameParam, Expression.Equals(FieldName.String, null, "Smith"));
            var nameBranch2 = new SearchParameterExpression(NameParam, Expression.Equals(FieldName.String, null, "Jones"));
            var unionPredicate = Expression.Union(UnionOperator.All, new Expression[] { nameBranch1, nameBranch2 });
            var tableExpr = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                unionPredicate,
                SearchParamTableExpressionKind.Normal);
            var root = SqlRootExpression.WithSearchParamTableExpressions(
                new List<SearchParamTableExpression> { tableExpr });
            var options = BuildSortOptions(BirthdateParam, SortOrder.Descending);

            var result = (SqlRootExpression)root.AcceptVisitor(_rewriter, options);

            Assert.False(options.IsSortWithFilter, "IsSortWithFilter should NOT be set when the union predicate does not cover the sort parameter.");
            Assert.Contains(result.SearchParamTableExpressions, e => e.Kind == SearchParamTableExpressionKind.Sort);
            Assert.DoesNotContain(result.SearchParamTableExpressions, e => e.Kind == SearchParamTableExpressionKind.SortWithFilter);
        }

        [Fact]
        public void GivenUnionPredicateWhereOnlyOneBranchMatchesSortParam_WhenRewritten_ThenNoMatchAndSortEmitted()
        {
            // A mixed union (one branch matches, one does not) must NOT signal "found".
            // All branches must match for SortWithFilter to be emitted.
            var birthdateBranch = new SearchParameterExpression(BirthdateParam, Expression.Equals(FieldName.DateTimeEnd, null, EndOfDay));
            var nameBranch = new SearchParameterExpression(NameParam, Expression.Equals(FieldName.String, null, "Smith"));
            var unionPredicate = Expression.Union(UnionOperator.All, new Expression[] { birthdateBranch, nameBranch });
            var tableExpr = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                unionPredicate,
                SearchParamTableExpressionKind.Normal);
            var root = SqlRootExpression.WithSearchParamTableExpressions(
                new List<SearchParamTableExpression> { tableExpr });
            var options = BuildSortOptions(BirthdateParam, SortOrder.Descending);

            var result = (SqlRootExpression)root.AcceptVisitor(_rewriter, options);

            Assert.False(options.IsSortWithFilter, "Partial branch match is not sufficient — all branches must cover the sort parameter.");
            Assert.Contains(result.SearchParamTableExpressions, e => e.Kind == SearchParamTableExpressionKind.Sort);
            Assert.DoesNotContain(result.SearchParamTableExpressions, e => e.Kind == SearchParamTableExpressionKind.SortWithFilter);
        }

        [Fact]
        public void GivenPlainSearchParameterPredicateMatchingSortParam_WhenRewritten_ThenMatchFoundAndSortWithFilterEmitted()
        {
            // Baseline: verify the existing null-signal path still works for plain SearchParameterExpression.
            var predicate = new SearchParameterExpression(
                BirthdateParam,
                Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, StartOfDay));
            var tableExpr = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                predicate,
                SearchParamTableExpressionKind.Normal);
            var root = SqlRootExpression.WithSearchParamTableExpressions(
                new List<SearchParamTableExpression> { tableExpr });
            var options = BuildSortOptions(BirthdateParam);

            var result = (SqlRootExpression)root.AcceptVisitor(_rewriter, options);

            Assert.True(options.IsSortWithFilter);
            Assert.Contains(result.SearchParamTableExpressions, e => e.Kind == SearchParamTableExpressionKind.SortWithFilter);
            Assert.DoesNotContain(result.SearchParamTableExpressions, e => e.Kind == SearchParamTableExpressionKind.Sort);
            Assert.DoesNotContain(result.SearchParamTableExpressions, e => e.Kind == SearchParamTableExpressionKind.NotExists);
        }
    }
}
