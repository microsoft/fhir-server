// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
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
        private static readonly DateTimeOffset StartOfMonth = new DateTimeOffset(2016, 7, 1, 0, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset EndOfMonth = new DateTimeOffset(2016, 7, 31, 23, 59, 59, TimeSpan.Zero).AddTicks(9999999);
        private static readonly DateTimeOffset StartOfYear = new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset EndOfYear = new DateTimeOffset(2016, 12, 31, 23, 59, 59, TimeSpan.Zero).AddTicks(9999999);

        private static SearchParameterInfo BuildParam(bool isScalarTemporal, Uri url = null)
        {
            return new SearchParameterInfo(
                "birthdate",
                "birthdate",
                SearchParamType.Date,
                url ?? new Uri("http://hl7.org/fhir/SearchParameter/individual-birthdate"),
                expression: "Patient.birthDate",
                baseResourceTypes: new[] { "Patient" })
            {
                IsDateOnly = isScalarTemporal,
                IsScalarTemporal = isScalarTemporal,
            };
        }

        private static MultiaryExpression EqualityPattern(DateTimeOffset start, DateTimeOffset end) =>
            (MultiaryExpression)Expression.And(
                Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, start),
                Expression.LessThanOrEqual(FieldName.DateTimeEnd, null, end));

        [Fact]
        public void GivenAllowListedScalarTemporalExactDay_WhenRewritten_ThenCollapsedToSingleEndDateTimeEquality()
        {
            var expr = new SearchParameterExpression(BuildParam(isScalarTemporal: true), EqualityPattern(StartOfDay, EndOfDay));

            var result = (SearchParameterExpression)expr.AcceptVisitor(ScalarTemporalEqualityRewriter.Instance, null);

            var rewritten = Assert.IsType<SearchParameterExpression>(result);
            var binary = Assert.IsType<BinaryExpression>(rewritten.Expression);
            Assert.Equal(FieldName.DateTimeEnd, binary.FieldName);
            Assert.Equal(BinaryOperator.Equal, binary.BinaryOperator);
            Assert.Equal(EndOfDay, binary.Value);
        }

        [Fact]
        public void GivenAllowListedScalarTemporalMonth_WhenRewritten_ThenCollapsedToEndDateTimeRange()
        {
            var expr = new SearchParameterExpression(BuildParam(isScalarTemporal: true), EqualityPattern(StartOfMonth, EndOfMonth));

            var result = (SearchParameterExpression)expr.AcceptVisitor(ScalarTemporalEqualityRewriter.Instance, null);

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
                    Assert.Equal(StartOfMonth, binary.Value);
                },
                second =>
                {
                    var binary = Assert.IsType<BinaryExpression>(second);
                    Assert.Equal(FieldName.DateTimeEnd, binary.FieldName);
                    Assert.Equal(BinaryOperator.LessThanOrEqual, binary.BinaryOperator);
                    Assert.Equal(EndOfMonth, binary.Value);
                });
        }

        [Fact]
        public void GivenAllowListedScalarTemporalYear_WhenRewritten_ThenCollapsedToEndDateTimeRange()
        {
            var expr = new SearchParameterExpression(BuildParam(isScalarTemporal: true), EqualityPattern(StartOfYear, EndOfYear));

            var result = (SearchParameterExpression)expr.AcceptVisitor(ScalarTemporalEqualityRewriter.Instance, null);

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
        public void GivenScalarTemporalParameterNotAllowListed_WhenEqualityPatternMatched_ThenPassThrough()
        {
            var expr = new SearchParameterExpression(
                BuildParam(isScalarTemporal: true, new Uri("http://example.org/SearchParameter/test-date")),
                EqualityPattern(StartOfYear, EndOfYear));

            var result = (SearchParameterExpression)expr.AcceptVisitor(ScalarTemporalEqualityRewriter.Instance, null);

            Assert.Same(expr, result);
        }

        [Fact]
        public void GivenNonScalarTemporalAllowListedParameter_WhenEqualityPatternMatched_ThenPassThrough()
        {
            var expr = new SearchParameterExpression(BuildParam(isScalarTemporal: false), EqualityPattern(StartOfYear, EndOfYear));

            var result = (SearchParameterExpression)expr.AcceptVisitor(ScalarTemporalEqualityRewriter.Instance, null);

            Assert.Same(expr, result);
        }

        [Fact]
        public void GivenDateOnlyButNotScalarTemporalAllowListedParameter_WhenEqualityPatternMatched_ThenPassThrough()
        {
            var parameter = BuildParam(isScalarTemporal: false);
            parameter.IsDateOnly = true;
            var expr = new SearchParameterExpression(parameter, EqualityPattern(StartOfYear, EndOfYear));

            var result = (SearchParameterExpression)expr.AcceptVisitor(ScalarTemporalEqualityRewriter.Instance, null);

            Assert.Same(expr, result);
        }

        [Fact]
        public void GivenSingleSidedPredicate_WhenRewritten_ThenPassThrough()
        {
            var single = Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, StartOfDay);
            var expr = new SearchParameterExpression(BuildParam(isScalarTemporal: true), single);

            var result = (SearchParameterExpression)expr.AcceptVisitor(ScalarTemporalEqualityRewriter.Instance, null);

            Assert.Same(expr, result);
        }

        [Fact]
        public void GivenRangeOperatorPattern_WhenRewritten_ThenPassThrough()
        {
            var range = Expression.GreaterThan(FieldName.DateTimeStart, null, EndOfDay);
            var expr = new SearchParameterExpression(BuildParam(isScalarTemporal: true), range);

            var result = (SearchParameterExpression)expr.AcceptVisitor(ScalarTemporalEqualityRewriter.Instance, null);

            Assert.Same(expr, result);
        }

        [Fact]
        public void GivenApproximateExpression_WhenRewritten_ThenPassThrough()
        {
            // Simulate the AST shape that Ap produces: same structure as Eq but with constants
            // shifted by an approximate delta (Start moved earlier, End moved later).
            // The rewriter must not collapse this; the resulting End value would not match any stored row.
            var approxStart = StartOfDay.AddDays(-30);
            var approxEnd = EndOfDay.AddDays(30);
            var expr = new SearchParameterExpression(BuildParam(isScalarTemporal: true), EqualityPattern(approxStart, approxEnd));

            var result = (SearchParameterExpression)expr.AcceptVisitor(ScalarTemporalEqualityRewriter.Instance, null);

            Assert.Same(expr, result);
        }

        [Fact]
        public void GivenCompositeParameter_WhenEqualityPatternMatched_ThenPassThrough()
        {
            var composite = new SearchParameterInfo(
                "Observation-code-value-date",
                "code-value-date",
                SearchParamType.Composite,
                new Uri("http://hl7.org/fhir/SearchParameter/Observation-code-value-date"),
                expression: "Observation",
                baseResourceTypes: new[] { "Observation" })
            {
                IsScalarTemporal = true,
            };
            var expr = new SearchParameterExpression(composite, EqualityPattern(StartOfDay, EndOfDay));

            var result = (SearchParameterExpression)expr.AcceptVisitor(ScalarTemporalEqualityRewriter.Instance, null);

            Assert.Same(expr, result);
        }
    }
}
