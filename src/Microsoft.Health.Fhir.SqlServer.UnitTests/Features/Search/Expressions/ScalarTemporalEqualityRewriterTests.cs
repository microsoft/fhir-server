// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class ScalarTemporalEqualityRewriterTests
    {
        private static readonly DateTimeOffset StartOfDay = new DateTimeOffset(2016, 7, 6, 0, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset EndOfDay = new DateTimeOffset(2016, 7, 6, 23, 59, 59, TimeSpan.Zero).AddTicks(9999999);
        private static readonly DateTimeOffset StartOfLastDayOfMonth = new DateTimeOffset(2016, 7, 31, 0, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset EndOfLastDayOfMonth = new DateTimeOffset(2016, 7, 31, 23, 59, 59, TimeSpan.Zero).AddTicks(9999999);
        private static readonly DateTimeOffset StartOfLastDayOfYear = new DateTimeOffset(2016, 12, 31, 0, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset EndOfLastDayOfYear = new DateTimeOffset(2016, 12, 31, 23, 59, 59, TimeSpan.Zero).AddTicks(9999999);
        private static readonly DateTimeOffset StartOfMonth = new DateTimeOffset(2016, 7, 1, 0, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset EndOfMonth = new DateTimeOffset(2016, 7, 31, 23, 59, 59, TimeSpan.Zero).AddTicks(9999999);

        public static TheoryData<DateTimeOffset, DateTimeOffset> ExactDayDates => new()
        {
            { StartOfDay, EndOfDay },
            { StartOfLastDayOfMonth, EndOfLastDayOfMonth },
            { StartOfLastDayOfYear, EndOfLastDayOfYear },
        };

        public static TheoryData<Expression> NonRewritableExpressions => new()
        {
            EqualityPattern(StartOfMonth, EndOfMonth), // month precision
            Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, StartOfDay), // single-sided predicate
            Expression.GreaterThan(FieldName.DateTimeStart, null, EndOfDay), // range operator
            EqualityPattern(StartOfDay.AddDays(-30), EndOfDay.AddDays(30)), // approximate / multi-day window
        };

        public static TheoryData<SearchParameterInfo> NonAllowListedParameters => new()
        {
            BuildBirthdateParam(new Uri("http://example.org/SearchParameter/test-date")),
            BuildBirthdateParam(searchParamType: SearchParamType.String),
            new SearchParameterInfo(
                "Patient-code-birthdate",
                "code-birthdate",
                SearchParamType.Composite,
                new Uri("http://example.org/SearchParameter/Patient-code-birthdate"),
                expression: "Patient",
                baseResourceTypes: new[] { "Patient" }),
        };

        private static SearchParameterInfo BuildBirthdateParam(Uri url = null, SearchParamType searchParamType = SearchParamType.Date)
        {
            return new SearchParameterInfo(
                "birthdate",
                "birthdate",
                searchParamType,
                url ?? new Uri("http://hl7.org/fhir/SearchParameter/individual-birthdate"),
                expression: "Patient.birthDate",
                baseResourceTypes: new[] { "Patient" });
        }

        private static SearchParameterInfo BuildReferenceParam()
        {
            return new SearchParameterInfo(
                "Observation-patient",
                "patient",
                SearchParamType.Reference,
                new Uri("http://hl7.org/fhir/SearchParameter/Observation-patient"),
                expression: "Observation.subject",
                baseResourceTypes: new[] { "Observation" },
                targetResourceTypes: new[] { "Patient" });
        }

        private static ChainedExpression BuildChainedExpression(Expression inner, bool reversed = false)
        {
            var expression = (ChainedExpression)RuntimeHelpers.GetUninitializedObject(typeof(ChainedExpression));
            SetBackingField(expression, nameof(ChainedExpression.ResourceTypes), new[] { "Observation" });
            SetBackingField(expression, nameof(ChainedExpression.ReferenceSearchParameter), BuildReferenceParam());
            SetBackingField(expression, nameof(ChainedExpression.TargetResourceTypes), new[] { "Patient" });
            SetBackingField(expression, nameof(ChainedExpression.Reversed), reversed);
            SetBackingField(expression, nameof(ChainedExpression.Expression), inner);
            return expression;
        }

        private static void SetBackingField<T>(ChainedExpression expression, string propertyName, T value)
        {
            FieldInfo field = typeof(ChainedExpression).GetField($"<{propertyName}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            field.SetValue(expression, value);
        }

        private static MultiaryExpression EqualityPattern(DateTimeOffset start, DateTimeOffset end) =>
            Expression.And(
                Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, start),
                Expression.LessThanOrEqual(FieldName.DateTimeEnd, null, end));

        [Theory]
        [MemberData(nameof(ExactDayDates))]
        public void GivenAllowListedBirthdateExactDay_WhenRewritten_ThenEmitsDaySplitUnion(DateTimeOffset start, DateTimeOffset end)
        {
            var expr = new SearchParameterExpression(BuildBirthdateParam(), EqualityPattern(start, end));

            var result = expr.AcceptVisitor(ScalarTemporalEqualityRewriter.Instance);

            AssertDaySplitUnion(result, start, end);
        }

        [Fact]
        public void GivenAllowListedBirthdateExactDayReversedOperandOrder_WhenRewritten_ThenEmitsDaySplitUnion()
        {
            var reversedPattern = Expression.And(
                Expression.LessThanOrEqual(FieldName.DateTimeEnd, null, EndOfDay),
                Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, StartOfDay));
            var expr = new SearchParameterExpression(BuildBirthdateParam(), reversedPattern);

            var result = expr.AcceptVisitor(ScalarTemporalEqualityRewriter.Instance);

            AssertDaySplitUnion(result, StartOfDay, EndOfDay);
        }

        [Fact]
        public void GivenAllowListedBirthdateExactDayInChainedExpression_WhenRewritten_ThenPassThrough()
        {
            var inner = new SearchParameterExpression(BuildBirthdateParam(), EqualityPattern(StartOfDay, EndOfDay));
            var expr = BuildChainedExpression(inner);

            var result = Assert.IsType<ChainedExpression>(expr.AcceptVisitor(ScalarTemporalEqualityRewriter.Instance));

            Assert.Same(inner, result.Expression);
        }

        [Fact]
        public void GivenTopLevelBirthdateExactDayBesideForwardChain_WhenRewritten_ThenPassThroughWithoutUnion()
        {
            // The failing-shape: an eligible top-level birthdate equality sitting BESIDE a chain (not nested in it).
            var birthdate = new SearchParameterExpression(BuildBirthdateParam(), EqualityPattern(StartOfDay, EndOfDay));
            var chain = BuildChainedExpression(Expression.GreaterThan(FieldName.DateTimeStart, null, EndOfDay), reversed: false);
            var tree = Expression.And(birthdate, chain);

            var result = ScalarTemporalEqualityRewriter.Rewrite(tree);

            // Any chain in the tree makes the whole query ineligible: nothing is rewritten, no UNION is produced.
            Assert.Same(tree, result);
        }

        [Fact]
        public void GivenTopLevelBirthdateExactDayBesideReverseChain_WhenRewritten_ThenPassThroughWithoutUnion()
        {
            // Reverse chains (_has) are ChainedExpression with Reversed=true; the same suppression must apply.
            var birthdate = new SearchParameterExpression(BuildBirthdateParam(), EqualityPattern(StartOfDay, EndOfDay));
            var reverseChain = BuildChainedExpression(Expression.GreaterThan(FieldName.DateTimeStart, null, EndOfDay), reversed: true);
            var tree = Expression.And(birthdate, reverseChain);

            var result = ScalarTemporalEqualityRewriter.Rewrite(tree);

            Assert.Same(tree, result);
        }

        [Fact]
        public void GivenAllowListedBirthdateExactDayWithoutChain_WhenRewrittenViaRewrite_ThenEmitsDaySplitUnion()
        {
            // No chain present, so Rewrite must still apply the day-split optimization (high-volume path preserved).
            var expr = new SearchParameterExpression(BuildBirthdateParam(), EqualityPattern(StartOfDay, EndOfDay));

            var result = ScalarTemporalEqualityRewriter.Rewrite(expr);

            AssertDaySplitUnion(result, StartOfDay, EndOfDay);
        }

        [Theory]
        [MemberData(nameof(NonRewritableExpressions))]
        public void GivenAllowListedBirthdateWithNonExactDayExpression_WhenRewritten_ThenPassThrough(Expression inner)
        {
            var expr = new SearchParameterExpression(BuildBirthdateParam(), inner);

            var result = Assert.IsType<SearchParameterExpression>(expr.AcceptVisitor(ScalarTemporalEqualityRewriter.Instance));

            Assert.Same(expr, result);
        }

        [Theory]
        [MemberData(nameof(NonAllowListedParameters))]
        public void GivenNonAllowListedParameter_WhenEqualityPatternMatched_ThenPassThrough(SearchParameterInfo param)
        {
            var expr = new SearchParameterExpression(param, EqualityPattern(StartOfDay, EndOfDay));

            var result = Assert.IsType<SearchParameterExpression>(expr.AcceptVisitor(ScalarTemporalEqualityRewriter.Instance));

            Assert.Same(expr, result);
        }

        private static void AssertDaySplitUnion(Expression result, DateTimeOffset expectedStart, DateTimeOffset expectedEnd)
        {
            var union = Assert.IsType<UnionExpression>(result);
            Assert.Equal(UnionOperator.All, union.Operator);
            Assert.Collection(
                union.Expressions,
                shortBranch => AssertSearchParameterAnd(
                    shortBranch,
                    and =>
                    {
                        Assert.Collection(
                            and.Expressions,
                            longerFlag => AssertIsLongerThanADayEquals(longerFlag, false),
                            endEq =>
                            {
                                var binary = Assert.IsType<BinaryExpression>(endEq);
                                Assert.Equal(FieldName.DateTimeEnd, binary.FieldName);
                                Assert.Equal(BinaryOperator.Equal, binary.BinaryOperator);
                                Assert.Equal(expectedEnd, binary.Value);
                            });
                    }),
                longerBranch => AssertSearchParameterAnd(
                    longerBranch,
                    and =>
                    {
                        Assert.Collection(
                            and.Expressions,
                            longerFlag => AssertIsLongerThanADayEquals(longerFlag, true),
                            startGe =>
                            {
                                var binary = Assert.IsType<BinaryExpression>(startGe);
                                Assert.Equal(FieldName.DateTimeStart, binary.FieldName);
                                Assert.Equal(BinaryOperator.GreaterThanOrEqual, binary.BinaryOperator);
                                Assert.Equal(expectedStart, binary.Value);
                            },
                            endLe =>
                            {
                                var binary = Assert.IsType<BinaryExpression>(endLe);
                                Assert.Equal(FieldName.DateTimeEnd, binary.FieldName);
                                Assert.Equal(BinaryOperator.LessThanOrEqual, binary.BinaryOperator);
                                Assert.Equal(expectedEnd, binary.Value);
                            });
                    }));
        }

        private static void AssertSearchParameterAnd(Expression branch, Action<MultiaryExpression> assertAnd)
        {
            var searchParameter = Assert.IsType<SearchParameterExpression>(branch);
            var and = Assert.IsType<MultiaryExpression>(searchParameter.Expression);
            Assert.Equal(MultiaryOperator.And, and.MultiaryOperation);
            assertAnd(and);
        }

        private static void AssertIsLongerThanADayEquals(Expression expr, bool expected)
        {
            var binary = Assert.IsType<BinaryExpression>(expr);
            Assert.Equal(SqlFieldName.DateTimeIsLongerThanADay, binary.FieldName);
            Assert.Equal(BinaryOperator.Equal, binary.BinaryOperator);
            Assert.Equal(expected, binary.Value);
        }
    }
}
