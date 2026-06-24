// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SearchParamTableExpressionExtensionsTests
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

        public static TheoryData<Expression, bool> UnionDetectionCases => new()
        {
            { BuildBareUnion(), true },
            { Expression.And(BuildBareUnion()), true },
            { Expression.And(BuildSmartV2Union(), new SearchParameterExpression(NameParam, Expression.Equals(FieldName.TokenCode, null, "Smith"))), true },
            { new SearchParameterExpression(NameParam, Expression.Equals(FieldName.TokenCode, null, "Smith")), false },
        };

        private static UnionExpression BuildBareUnion()
        {
            var endOfDay = new DateTimeOffset(2016, 7, 6, 23, 59, 59, TimeSpan.Zero);
            var startOfDay = new DateTimeOffset(2016, 7, 6, 0, 0, 0, TimeSpan.Zero);
            var shortBranch = new SearchParameterExpression(BirthdateParam, Expression.Equals(FieldName.DateTimeEnd, null, endOfDay));
            var longBranch = new SearchParameterExpression(BirthdateParam, Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, startOfDay));
            return Expression.Union(UnionOperator.All, new Expression[] { shortBranch, longBranch });
        }

        private static UnionExpression BuildSmartV2Union()
        {
            var branch = new SearchParameterExpression(NameParam, Expression.Equals(FieldName.TokenCode, null, "Smith"));
            return Expression.Union(UnionOperator.All, new Expression[] { branch });
        }

        [Theory]
        [MemberData(nameof(UnionDetectionCases))]
        public void HasUnionAllExpression_DetectsBareAndNestedUnions(Expression predicate, bool expected)
        {
            var tableExpr = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                predicate,
                SearchParamTableExpressionKind.Normal);

            Assert.Equal(expected, tableExpr.HasUnionAllExpression());
        }

        [Fact]
        public void SplitExpressions_WhenPredicateContainsSingleUnionChild_ReturnsTrueWithUnionAndNullRemainder()
        {
            var union = BuildBareUnion();
            var tableExpr = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                Expression.And(union),
                SearchParamTableExpressionKind.Normal);

            bool result = tableExpr.SplitExpressions(out UnionExpression outUnion, out SearchParamTableExpression outRemainder);

            Assert.True(result);
            Assert.Same(union, outUnion);
            Assert.Null(outRemainder);
        }

        [Fact]
        public void SplitExpressions_WhenPredicateIsBareUnion_ReturnsTrueWithUnionAndNullRemainder()
        {
            var union = BuildBareUnion();
            var tableExpr = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                union,
                SearchParamTableExpressionKind.Normal);

            bool result = tableExpr.SplitExpressions(out UnionExpression outUnion, out SearchParamTableExpression outRemainder);

            Assert.True(result);
            Assert.Same(union, outUnion);
            Assert.Null(outRemainder);
        }

        [Fact]
        public void SplitExpressions_WhenUnionIsMarkedAsNoSplit_StillSeparatesUnion()
        {
            // DoNotSplitIntoSeparateCtes controls only how the union is lowered to SQL (a single inline UNION ALL CTE).
            // It no longer prevents the union from being separated from its siblings, so the union is still returned.
            var union = BuildBareUnion();
            union.DoNotSplitIntoSeparateCtes = true;

            var tableExpr = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                Expression.And(union),
                SearchParamTableExpressionKind.Normal);

            bool result = tableExpr.SplitExpressions(out UnionExpression outUnion, out SearchParamTableExpression outRemainder);

            Assert.True(result);
            Assert.Same(union, outUnion);
            Assert.True(outUnion.DoNotSplitIntoSeparateCtes);
            Assert.Null(outRemainder);
        }

        [Fact]
        public void SplitExpressions_WhenUnionNestedInMultiaryWithSiblings_ReturnsTrueWithUnionAndRemainder()
        {
            var union = BuildSmartV2Union();
            var otherExpr = new SearchParameterExpression(NameParam, Expression.Equals(FieldName.TokenCode, null, "Smith"));
            var multiary = Expression.And(union, otherExpr);

            var tableExpr = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                multiary,
                SearchParamTableExpressionKind.Normal);

            bool result = tableExpr.SplitExpressions(out UnionExpression outUnion, out SearchParamTableExpression outRemainder);

            Assert.True(result);
            Assert.Same(union, outUnion);
            Assert.NotNull(outRemainder);
            var remainderAnd = Assert.IsType<MultiaryExpression>(outRemainder.Predicate);
            Assert.DoesNotContain(remainderAnd.Expressions, e => e is UnionExpression);
        }

        [Fact]
        public void SplitExpressions_WhenPredicateIsPlainSearchParameter_ReturnsFalse()
        {
            var predicate = new SearchParameterExpression(NameParam, Expression.Equals(FieldName.TokenCode, null, "Smith"));
            var tableExpr = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                predicate,
                SearchParamTableExpressionKind.Normal);

            bool result = tableExpr.SplitExpressions(out UnionExpression outUnion, out SearchParamTableExpression outRemainder);

            Assert.False(result);
            Assert.Null(outUnion);
            Assert.Null(outRemainder);
        }

        [Fact]
        public void SortExpressionsByQueryLogic_WhenRegularUnionPrecedesConcatenationPair_ThenUnionMovesFirstAndPairStaysAdjacent()
        {
            var leadingNormal = BuildNormalTableExpression("leading");
            var regularUnion = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                Expression.And(BuildBareUnion()),
                SearchParamTableExpressionKind.Normal);
            var normalSibling = BuildNormalTableExpression("sibling-normal");
            var concatenationSibling = new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                new SearchParameterExpression(NameParam, Expression.Equals(FieldName.TokenCode, null, "sibling-concat")),
                SearchParamTableExpressionKind.Concatenation);
            var trailingNormal = BuildNormalTableExpression("trailing");

            var input = new List<SearchParamTableExpression>
            {
                leadingNormal,
                regularUnion,
                normalSibling,
                concatenationSibling,
                trailingNormal,
            };

            IReadOnlyList<SearchParamTableExpression> sorted = input.SortExpressionsByQueryLogic();

            Assert.Same(regularUnion, sorted[0]);
            Assert.Same(leadingNormal, sorted[1]);
            Assert.Same(normalSibling, sorted[2]);
            Assert.Same(concatenationSibling, sorted[3]);
            Assert.Same(trailingNormal, sorted[4]);
            Assert.Equal(SearchParamTableExpressionKind.Concatenation, sorted[3].Kind);
        }

        private static SearchParamTableExpression BuildNormalTableExpression(string code)
        {
            return new SearchParamTableExpression(
                DateTimeQueryGenerator.Instance,
                new SearchParameterExpression(NameParam, Expression.Equals(FieldName.TokenCode, null, code)),
                SearchParamTableExpressionKind.Normal);
        }
    }
}
