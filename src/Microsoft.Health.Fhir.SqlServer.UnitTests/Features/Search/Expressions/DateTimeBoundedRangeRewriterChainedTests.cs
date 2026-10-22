// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class DateTimeBoundedRangeRewriterChainedTests
    {
        private static readonly DateTimeOffset Low = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset High = Low.AddDays(1).AddTicks(-1);

        [Theory]
        [InlineData(1, null, false, false, false)]
        [InlineData(2, null, true, true, true)]
        [InlineData(1, 1, false, true, true)]
        [InlineData(2, 1, true, false, false)]
        public void GivenChainedBirthdateOverlap_WhenRewritten_ThenSingleTableContainsExactUnionPredicate(
            int chainLevel,
            int? componentIndex,
            bool strictLow,
            bool strictHigh,
            bool reversed)
        {
            var lower = new BinaryExpression(strictLow ? BinaryOperator.GreaterThan : BinaryOperator.GreaterThanOrEqual, FieldName.DateTimeEnd, componentIndex, Low);
            var upper = new BinaryExpression(strictHigh ? BinaryOperator.LessThan : BinaryOperator.LessThanOrEqual, FieldName.DateTimeStart, componentIndex, High);
            var predicate = Expression.SearchParameter(Birthdate(), reversed ? Expression.And(upper, lower) : Expression.And(lower, upper));
            var table = new SearchParamTableExpression(DateTimeQueryGenerator.Instance, predicate, SearchParamTableExpressionKind.Normal, chainLevel);
            var root = SqlRootExpression.WithSearchParamTableExpressions(table);

            var result = Rewrite(root);

            var rewritten = Assert.Single(result.SearchParamTableExpressions);
            Assert.Equal(SearchParamTableExpressionKind.Normal, rewritten.Kind);
            Assert.Equal(chainLevel, rewritten.ChainLevel);
            Assert.Same(table.QueryGenerator, rewritten.QueryGenerator);
            Assert.Same(root.ResourceTableExpressions, result.ResourceTableExpressions);
            var searchParameter = Assert.IsType<SearchParameterExpression>(rewritten.Predicate);
            Assert.Same(predicate.Parameter, searchParameter.Parameter);
            var overlap = Assert.IsType<MultiaryExpression>(searchParameter.Expression);
            Assert.Equal(MultiaryOperator.And, overlap.MultiaryOperation);
            Assert.Equal(3, overlap.Expressions.Count);
            AssertBinary(overlap.Expressions[0], FieldName.DateTimeEnd, lower.BinaryOperator, Low, componentIndex);
            AssertBinary(overlap.Expressions[1], FieldName.DateTimeStart, upper.BinaryOperator, High, componentIndex);
            var alternatives = Assert.IsType<MultiaryExpression>(overlap.Expressions[2]);
            Assert.Equal(MultiaryOperator.Or, alternatives.MultiaryOperation);
            Assert.Equal(2, alternatives.Expressions.Count);
            AssertBinary(alternatives.Expressions[0], SqlFieldName.DateTimeIsLongerThanADay, BinaryOperator.Equal, true, componentIndex);
            AssertBinary(alternatives.Expressions[1], FieldName.DateTimeStart, lower.BinaryOperator, Low.AddDays(-1), componentIndex);
            Assert.Same(result, Rewrite(result));
        }

        [Theory]
        [InlineData(0, true, BinaryOperator.GreaterThanOrEqual)]
        [InlineData(12, true, BinaryOperator.GreaterThanOrEqual)]
        [InlineData(24, true, BinaryOperator.GreaterThan)]
        [InlineData(0, false, BinaryOperator.GreaterThanOrEqual)]
        public void GivenMinimumLowerBound_WhenRewritten_ThenClampingAndStrictOperatorMatchLegacy(
            int hoursAfterMinimum,
            bool strict,
            BinaryOperator expectedOperator)
        {
            var low = DateTimeOffset.MinValue.AddHours(hoursAfterMinimum);
            var predicate = Expression.SearchParameter(
                Birthdate(),
                Expression.And(
                    new BinaryExpression(strict ? BinaryOperator.GreaterThan : BinaryOperator.GreaterThanOrEqual, FieldName.DateTimeEnd, null, low),
                    Expression.LessThan(FieldName.DateTimeStart, null, low.AddHours(12))));

            var result = Rewrite(Root(predicate));

            var rewritten = Assert.Single(result.SearchParamTableExpressions);
            var searchParameter = Assert.IsType<SearchParameterExpression>(rewritten.Predicate);
            var overlap = Assert.IsType<MultiaryExpression>(searchParameter.Expression);
            var alternatives = Assert.IsType<MultiaryExpression>(overlap.Expressions[2]);
            AssertBinary(alternatives.Expressions[1], FieldName.DateTimeStart, expectedOperator, DateTimeOffset.MinValue);
        }

        [Theory]
        [InlineData(0, true, true, SearchParamType.Date)]
        [InlineData(-1, true, true, SearchParamType.Date)]
        [InlineData(1, false, true, SearchParamType.Date)]
        [InlineData(1, true, false, SearchParamType.Date)]
        [InlineData(1, true, true, SearchParamType.Composite)]
        public void GivenIneligibleTable_WhenRewritten_ThenLegacySplitIsPreserved(
            int chainLevel,
            bool allowListed,
            bool dateTimeGenerator,
            SearchParamType type)
        {
            var parameter = Birthdate(allowListed ? null : new Uri("http://example.org/other-date"), type);
            var root = Root(Expression.SearchParameter(parameter, Overlap()), chainLevel, dateTimeGenerator ? DateTimeQueryGenerator.Instance : null);

            var result = Rewrite(root);

            if (type == SearchParamType.Composite)
            {
                Assert.Same(root, result);
            }
            else
            {
                Assert.Equal(2, result.SearchParamTableExpressions.Count);
                Assert.Equal(SearchParamTableExpressionKind.Normal, result.SearchParamTableExpressions[0].Kind);
                Assert.Equal(SearchParamTableExpressionKind.Concatenation, result.SearchParamTableExpressions[1].Kind);
                Assert.All(result.SearchParamTableExpressions, table => Assert.Equal(chainLevel, table.ChainLevel));
            }
        }

        [Theory]
        [InlineData((int)SearchParamTableExpressionKind.Chain)]
        [InlineData((int)SearchParamTableExpressionKind.Include)]
        [InlineData((int)SearchParamTableExpressionKind.Sort)]
        [InlineData((int)SearchParamTableExpressionKind.All)]
        [InlineData((int)SearchParamTableExpressionKind.Concatenation)]
        public void GivenNonNormalTable_WhenRewritten_ThenExistingBehaviorIsPreserved(int kindValue)
        {
            var kind = (SearchParamTableExpressionKind)kindValue;
            var table = new SearchParamTableExpression(DateTimeQueryGenerator.Instance, Expression.SearchParameter(Birthdate(), Overlap()), kind, 1);
            var root = SqlRootExpression.WithSearchParamTableExpressions(table);

            var result = Rewrite(root);

            if (kind == SearchParamTableExpressionKind.Concatenation)
            {
                Assert.Equal(2, result.SearchParamTableExpressions.Count);
            }
            else
            {
                Assert.Same(root, result);
            }
        }

        [Fact]
        public void GivenIncompatibleComponentsOrMissingWrapper_WhenRewritten_ThenLegacySplitIsPreserved()
        {
            var incompatible = Expression.SearchParameter(
                Birthdate(),
                Expression.And(
                    Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, 0, Low),
                    Expression.LessThanOrEqual(FieldName.DateTimeStart, 1, High)));

            Assert.Equal(2, Rewrite(Root(incompatible)).SearchParamTableExpressions.Count);
            Assert.Equal(2, Rewrite(Root(Overlap())).SearchParamTableExpressions.Count);
        }

        [Fact]
        public void GivenDateParameterWithComponentsOrDifferentGenerator_WhenRewritten_ThenLegacySplitIsPreserved()
        {
            var parameter = new SearchParameterInfo(
                "birthdate",
                "birthdate",
                SearchParamType.Date,
                Birthdate().Url,
                components: [new SearchParameterComponentInfo(expression: "birthDate")]);
            var componentRoot = Root(Expression.SearchParameter(parameter, Overlap()));
            var generatorRoot = Root(Expression.SearchParameter(Birthdate(), Overlap()), 1, TokenQueryGenerator.Instance);

            Assert.Equal(2, Rewrite(componentRoot).SearchParamTableExpressions.Count);
            Assert.Equal(2, Rewrite(generatorRoot).SearchParamTableExpressions.Count);
        }

        [Fact]
        public void GivenContainmentOrScalarPath_WhenRewritten_ThenPredicateIsUnchanged()
        {
            Expression[] predicates =
            [
                Expression.And(
                    Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, Low),
                    Expression.LessThanOrEqual(FieldName.DateTimeEnd, null, High)),
                Expression.And(
                    Expression.Equals(SqlFieldName.DateTimeIsLongerThanADay, null, false),
                    Expression.Equals(FieldName.DateTimeEnd, null, High)),
                Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, null, Low),
            ];

            foreach (var predicate in predicates)
            {
                var root = Root(Expression.SearchParameter(Birthdate(), predicate));

                Assert.Same(root, Rewrite(root));
            }
        }

        [Theory]
        [InlineData(-3, 12, false, false)]
        [InlineData(-0.75, 6, false, true)]
        [InlineData(-3, 12, true, true)]
        [InlineData(0, -1, true, false)]
        [InlineData(2, 72, true, false)]
        public void GivenHistoricalRows_WhenRewritten_ThenMatchesLegacyUnionIncludingMalformedFlags(
            double startDays,
            int endHours,
            bool isLong,
            bool expected)
        {
            var parameter = Expression.SearchParameter(Birthdate(), Overlap());
            var start = Low.AddDays(startDays);
            var end = Low.AddHours(endHours);
            var legacy = Rewrite(Root(parameter, chainLevel: 0));
            var result = Rewrite(Root(parameter));

            Assert.Single(result.SearchParamTableExpressions);
            bool legacyMatches = legacy.SearchParamTableExpressions.Any(table => Matches(table.Predicate, start, end, isLong));
            bool rewrittenMatches = Matches(result.SearchParamTableExpressions[0].Predicate, start, end, isLong);
            Assert.Equal(expected, legacyMatches);
            Assert.Equal(legacyMatches, rewrittenMatches);
        }

        [Fact]
        public void GivenMixedRoot_WhenRewritten_ThenOnlyEligibleTableChangesAndOrderIsPreserved()
        {
            var token = new SearchParamTableExpression(TokenQueryGenerator.Instance, Expression.Equals(FieldName.TokenCode, null, "code"), SearchParamTableExpressionKind.Normal);
            var direct = new SearchParamTableExpression(DateTimeQueryGenerator.Instance, Expression.SearchParameter(Birthdate(), Overlap()), SearchParamTableExpressionKind.Normal);
            var chained = Root(Expression.SearchParameter(Birthdate(), Overlap())).SearchParamTableExpressions[0];
            var top = new SearchParamTableExpression(null, Expression.Equals(FieldName.TokenCode, null, "code"), SearchParamTableExpressionKind.Top);
            var root = SqlRootExpression.WithSearchParamTableExpressions(token, direct, chained, top);

            var result = Rewrite(root);

            Assert.Equal(5, result.SearchParamTableExpressions.Count);
            Assert.Same(token, result.SearchParamTableExpressions[0]);
            Assert.Equal(0, result.SearchParamTableExpressions[1].ChainLevel);
            Assert.Equal(SearchParamTableExpressionKind.Concatenation, result.SearchParamTableExpressions[2].Kind);
            Assert.Equal(1, result.SearchParamTableExpressions[3].ChainLevel);
            Assert.Equal(SearchParamTableExpressionKind.Normal, result.SearchParamTableExpressions[3].Kind);
            Assert.Same(top, result.SearchParamTableExpressions[4]);
        }

        private static SearchParameterInfo Birthdate(Uri url = null, SearchParamType type = SearchParamType.Date) =>
            new SearchParameterInfo(
                "birthdate",
                "birthdate",
                type,
                url ?? new Uri("http://hl7.org/fhir/SearchParameter/individual-birthdate"),
                expression: "Patient.birthDate",
                baseResourceTypes: ["Patient"]);

        private static MultiaryExpression Overlap() =>
            Expression.And(
                Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, null, Low),
                Expression.LessThanOrEqual(FieldName.DateTimeStart, null, High));

        private static SqlRootExpression Root(Expression predicate, int chainLevel = 1) =>
            Root(predicate, chainLevel, DateTimeQueryGenerator.Instance);

        private static SqlRootExpression Root(Expression predicate, int chainLevel, SearchParamTableExpressionQueryGenerator generator) =>
            SqlRootExpression.WithSearchParamTableExpressions(new SearchParamTableExpression(generator, predicate, SearchParamTableExpressionKind.Normal, chainLevel));

        private static SqlRootExpression Rewrite(SqlRootExpression root) =>
            (SqlRootExpression)root.AcceptVisitor(DateTimeBoundedRangeRewriter.Instance, null);

        private static void AssertBinary(Expression expression, FieldName field, BinaryOperator op, object value, int? componentIndex = null)
        {
            var binary = Assert.IsType<BinaryExpression>(expression);
            Assert.Equal(field, binary.FieldName);
            Assert.Equal(op, binary.BinaryOperator);
            Assert.Equal(value, binary.Value);
            Assert.Equal(componentIndex, binary.ComponentIndex);
        }

        private static bool Matches(Expression expression, DateTimeOffset start, DateTimeOffset end, bool isLong)
        {
            if (expression is SearchParameterExpression searchParameter)
            {
                return Matches(searchParameter.Expression, start, end, isLong);
            }

            if (expression is MultiaryExpression multiary)
            {
                return multiary.MultiaryOperation == MultiaryOperator.And
                    ? multiary.Expressions.All(child => Matches(child, start, end, isLong))
                    : multiary.Expressions.Any(child => Matches(child, start, end, isLong));
            }

            var binary = Assert.IsType<BinaryExpression>(expression);
            if (binary.FieldName == SqlFieldName.DateTimeIsLongerThanADay)
            {
                Assert.Equal(BinaryOperator.Equal, binary.BinaryOperator);
                return isLong == (bool)binary.Value;
            }

            var actual = binary.FieldName == FieldName.DateTimeStart ? start : end;
            var expected = (DateTimeOffset)binary.Value;
            return binary.BinaryOperator switch
            {
                BinaryOperator.GreaterThan => actual > expected,
                BinaryOperator.GreaterThanOrEqual => actual >= expected,
                BinaryOperator.LessThan => actual < expected,
                BinaryOperator.LessThanOrEqual => actual <= expected,
                _ => throw new InvalidOperationException($"Unexpected operator {binary.BinaryOperator}"),
            };
        }
    }
}
