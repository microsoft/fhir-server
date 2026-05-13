// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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
        private static readonly DateTimeOffset StartOfYear = new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset EndOfYear = new DateTimeOffset(2016, 12, 31, 23, 59, 59, TimeSpan.Zero).AddTicks(9999999);

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

        private static MultiaryExpression EqualityPattern(DateTimeOffset start, DateTimeOffset end) =>
            (MultiaryExpression)Expression.And(
                Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, start),
                Expression.LessThanOrEqual(FieldName.DateTimeEnd, null, end));

        [Fact]
        public void GivenAllowListedBirthdateExactDay_WhenRewritten_ThenUsesEndDateTimeAndNotLongerThanDay()
        {
            var expr = new SearchParameterExpression(BuildBirthdateParam(), EqualityPattern(StartOfDay, EndOfDay));

            var result = expr.AcceptVisitor(ScalarTemporalEqualityRewriter.Instance, null);

            AssertDaySplitRewrite(result, StartOfDay, EndOfDay);
        }

        [Fact]
        public void GivenAllowListedBirthdateLastDayOfMonth_WhenRewritten_ThenUsesEndDateTimeAndNotLongerThanDay()
        {
            var expr = new SearchParameterExpression(BuildBirthdateParam(), EqualityPattern(StartOfLastDayOfMonth, EndOfLastDayOfMonth));

            var result = expr.AcceptVisitor(ScalarTemporalEqualityRewriter.Instance, null);

            AssertDaySplitRewrite(result, StartOfLastDayOfMonth, EndOfLastDayOfMonth);
        }

        [Fact]
        public void GivenAllowListedBirthdateLastDayOfYear_WhenRewritten_ThenUsesEndDateTimeAndNotLongerThanDay()
        {
            var expr = new SearchParameterExpression(BuildBirthdateParam(), EqualityPattern(StartOfLastDayOfYear, EndOfLastDayOfYear));

            var result = expr.AcceptVisitor(ScalarTemporalEqualityRewriter.Instance, null);

            AssertDaySplitRewrite(result, StartOfLastDayOfYear, EndOfLastDayOfYear);
        }

        [Fact]
        public void GivenAllowListedBirthdateMonth_WhenRewritten_ThenPassThrough()
        {
            var expr = new SearchParameterExpression(BuildBirthdateParam(), EqualityPattern(StartOfMonth, EndOfMonth));

            var result = Assert.IsType<SearchParameterExpression>(expr.AcceptVisitor(ScalarTemporalEqualityRewriter.Instance, null));

            Assert.Same(expr, result);
        }

        [Fact]
        public void GivenAllowListedBirthdateYear_WhenRewritten_ThenUsesEndDateTimeRange()
        {
            var expr = new SearchParameterExpression(BuildBirthdateParam(), EqualityPattern(StartOfYear, EndOfYear));

            var result = expr.AcceptVisitor(ScalarTemporalEqualityRewriter.Instance, null);

            var rewritten = Assert.IsType<SearchParameterExpression>(result);
            var multiary = Assert.IsType<MultiaryExpression>(rewritten.Expression);
            Assert.Equal(MultiaryOperator.And, multiary.MultiaryOperation);
            Assert.Collection(
                multiary.Expressions,
                first =>
                {
                    var binary = Assert.IsType<BinaryExpression>(first);
                    Assert.Equal(FieldName.DateTimeEnd, binary.FieldName);
                    Assert.Equal(BinaryOperator.GreaterThanOrEqual, binary.BinaryOperator);
                    Assert.Equal(StartOfYear, binary.Value);
                },
                second =>
                {
                    var binary = Assert.IsType<BinaryExpression>(second);
                    Assert.Equal(FieldName.DateTimeEnd, binary.FieldName);
                    Assert.Equal(BinaryOperator.LessThanOrEqual, binary.BinaryOperator);
                    Assert.Equal(EndOfYear, binary.Value);
                });
        }

        [Fact]
        public void GivenDateParameterNotAllowListed_WhenEqualityPatternMatched_ThenPassThrough()
        {
            var expr = new SearchParameterExpression(
                BuildBirthdateParam(new Uri("http://example.org/SearchParameter/test-date")),
                EqualityPattern(StartOfYear, EndOfYear));

            var result = Assert.IsType<SearchParameterExpression>(expr.AcceptVisitor(ScalarTemporalEqualityRewriter.Instance, null));

            Assert.Same(expr, result);
        }

        [Fact]
        public void GivenAllowListedNonDateParameter_WhenEqualityPatternMatched_ThenPassThrough()
        {
            var expr = new SearchParameterExpression(
                BuildBirthdateParam(searchParamType: SearchParamType.String),
                EqualityPattern(StartOfYear, EndOfYear));

            var result = Assert.IsType<SearchParameterExpression>(expr.AcceptVisitor(ScalarTemporalEqualityRewriter.Instance, null));

            Assert.Same(expr, result);
        }

        [Fact]
        public void GivenAllowListedBirthdateExactDayReversedOrder_WhenRewritten_ThenUsesEndDateTimeAndNotLongerThanDay()
        {
            var reversedPattern = (MultiaryExpression)Expression.And(
                Expression.LessThanOrEqual(FieldName.DateTimeEnd, null, EndOfDay),
                Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, StartOfDay));
            var expr = new SearchParameterExpression(BuildBirthdateParam(), reversedPattern);

            var result = expr.AcceptVisitor(ScalarTemporalEqualityRewriter.Instance, null);

            AssertDaySplitRewrite(result, StartOfDay, EndOfDay);
        }

        [Fact]
        public void GivenSingleSidedPredicate_WhenRewritten_ThenPassThrough()
        {
            var single = Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, StartOfDay);
            var expr = new SearchParameterExpression(BuildBirthdateParam(), single);

            var result = Assert.IsType<SearchParameterExpression>(expr.AcceptVisitor(ScalarTemporalEqualityRewriter.Instance, null));

            Assert.Same(expr, result);
        }

        [Fact]
        public void GivenRangeOperatorPattern_WhenRewritten_ThenPassThrough()
        {
            var range = Expression.GreaterThan(FieldName.DateTimeStart, null, EndOfDay);
            var expr = new SearchParameterExpression(BuildBirthdateParam(), range);

            var result = Assert.IsType<SearchParameterExpression>(expr.AcceptVisitor(ScalarTemporalEqualityRewriter.Instance, null));

            Assert.Same(expr, result);
        }

        [Fact]
        public void GivenApproximateExpression_WhenRewritten_ThenPassThrough()
        {
            var approxStart = StartOfDay.AddDays(-30);
            var approxEnd = EndOfDay.AddDays(30);
            var expr = new SearchParameterExpression(BuildBirthdateParam(), EqualityPattern(approxStart, approxEnd));

            var result = Assert.IsType<SearchParameterExpression>(expr.AcceptVisitor(ScalarTemporalEqualityRewriter.Instance, null));

            Assert.Same(expr, result);
        }

        [Fact]
        public void GivenCompositeParameter_WhenEqualityPatternMatched_ThenPassThrough()
        {
            var composite = new SearchParameterInfo(
                "Patient-code-birthdate",
                "code-birthdate",
                SearchParamType.Composite,
                new Uri("http://example.org/SearchParameter/Patient-code-birthdate"),
                expression: "Patient",
                baseResourceTypes: new[] { "Patient" });
            var expr = new SearchParameterExpression(composite, EqualityPattern(StartOfDay, EndOfDay));

            var result = Assert.IsType<SearchParameterExpression>(expr.AcceptVisitor(ScalarTemporalEqualityRewriter.Instance, null));

            Assert.Same(expr, result);
        }

        private static void AssertDaySplitRewrite(Expression result, DateTimeOffset expectedStart, DateTimeOffset expectedEnd)
        {
            var rewritten = Assert.IsType<SearchParameterExpression>(result);
            var or = Assert.IsType<MultiaryExpression>(rewritten.Expression);
            Assert.Equal(MultiaryOperator.Or, or.MultiaryOperation);
            Assert.Collection(
                or.Expressions,
                shortBranch =>
                {
                    var and = Assert.IsType<MultiaryExpression>(shortBranch);
                    Assert.Equal(MultiaryOperator.And, and.MultiaryOperation);
                    Assert.Collection(
                        and.Expressions,
                        longerFlag =>
                        {
                            var binary = Assert.IsType<BinaryExpression>(longerFlag);
                            Assert.Equal(SqlFieldName.DateTimeIsLongerThanADay, binary.FieldName);
                            Assert.Equal(BinaryOperator.Equal, binary.BinaryOperator);
                            Assert.False((bool)binary.Value);
                        },
                        endEq =>
                        {
                            var binary = Assert.IsType<BinaryExpression>(endEq);
                            Assert.Equal(FieldName.DateTimeEnd, binary.FieldName);
                            Assert.Equal(BinaryOperator.Equal, binary.BinaryOperator);
                            Assert.Equal(expectedEnd, binary.Value);
                        });
                },
                longerBranch =>
                {
                    var and = Assert.IsType<MultiaryExpression>(longerBranch);
                    Assert.Equal(MultiaryOperator.And, and.MultiaryOperation);
                    Assert.Collection(
                        and.Expressions,
                        longerFlag =>
                        {
                            var binary = Assert.IsType<BinaryExpression>(longerFlag);
                            Assert.Equal(SqlFieldName.DateTimeIsLongerThanADay, binary.FieldName);
                            Assert.Equal(BinaryOperator.Equal, binary.BinaryOperator);
                            Assert.True((bool)binary.Value);
                        },
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
                });
        }
    }
}
