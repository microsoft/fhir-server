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

        // SortWithFilter requires the sort parameter on every branch; otherwise this falls back to Sort.
        // Not-matched cases use descending order with no continuation token so VisitSqlRoot appends Sort, not NotExists.
        public static TheoryData<Expression, SortOrder, bool> SortCoverageCases => new()
        {
            // All branches of the day-split union reference birthdate → match.
            { BuildBirthdateDaySplitUnion(), SortOrder.Ascending, true },

            // No branch references birthdate → no match.
            {
                Expression.Union(
                    UnionOperator.All,
                    new Expression[]
                    {
                        new SearchParameterExpression(NameParam, Expression.Equals(FieldName.String, null, "Smith")),
                        new SearchParameterExpression(NameParam, Expression.Equals(FieldName.String, null, "Jones")),
                    }),
                SortOrder.Descending,
                false
            },

            // Mixed union: one branch references birthdate, one does not → no match (all branches must match).
            {
                Expression.Union(
                    UnionOperator.All,
                    new Expression[]
                    {
                        new SearchParameterExpression(BirthdateParam, Expression.Equals(FieldName.DateTimeEnd, null, EndOfDay)),
                        new SearchParameterExpression(NameParam, Expression.Equals(FieldName.String, null, "Smith")),
                    }),
                SortOrder.Descending,
                false
            },

            // Baseline plain SearchParameterExpression on birthdate → match (existing null-signal path).
            {
                new SearchParameterExpression(BirthdateParam, Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, StartOfDay)),
                SortOrder.Ascending,
                true
            },
        };

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

        [Theory]
        [MemberData(nameof(SortCoverageCases))]
        public void GivenSortPredicateCoverage_WhenRewritten_ThenSortWithFilterEmittedOnlyWhenAllBranchesMatch(Expression predicate, SortOrder sortOrder, bool expectMatch)
        {
            var tableExpr = new SearchParamTableExpression(DateTimeQueryGenerator.Instance, predicate, SearchParamTableExpressionKind.Normal);
            var root = SqlRootExpression.WithSearchParamTableExpressions(new List<SearchParamTableExpression> { tableExpr });
            var options = BuildSortOptions(BirthdateParam, sortOrder);

            var result = (SqlRootExpression)root.AcceptVisitor(_rewriter, options);

            SearchParamTableExpressionKind expected = expectMatch ? SearchParamTableExpressionKind.SortWithFilter : SearchParamTableExpressionKind.Sort;
            SearchParamTableExpressionKind unexpected = expectMatch ? SearchParamTableExpressionKind.Sort : SearchParamTableExpressionKind.SortWithFilter;

            Assert.Equal(expectMatch, options.IsSortWithFilter);
            Assert.Contains(result.SearchParamTableExpressions, e => e.Kind == expected);
            Assert.DoesNotContain(result.SearchParamTableExpressions, e => e.Kind == unexpected);
            Assert.DoesNotContain(result.SearchParamTableExpressions, e => e.Kind == SearchParamTableExpressionKind.NotExists);
        }
    }
}
