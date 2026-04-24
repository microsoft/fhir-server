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
    public class DateOnlyEqualityRewriterTests
    {
        private static readonly DateTimeOffset StartOfDay = new DateTimeOffset(2016, 7, 6, 0, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset EndOfDay = new DateTimeOffset(2016, 7, 6, 23, 59, 59, TimeSpan.Zero).AddTicks(9999999);

        private static SearchParameterInfo BuildParam(bool isDateOnly)
        {
            var sp = new SearchParameterInfo(
                "birthdate",
                "birthdate",
                SearchParamType.Date,
                new Uri("http://hl7.org/fhir/SearchParameter/individual-birthdate"),
                expression: "Patient.birthDate",
                baseResourceTypes: new[] { "Patient" })
            {
                IsDateOnly = isDateOnly,
            };
            return sp;
        }

        private static MultiaryExpression EqualityPattern() =>
            (MultiaryExpression)Expression.And(
                Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, StartOfDay),
                Expression.LessThanOrEqual(FieldName.DateTimeEnd, null, EndOfDay));

        [Fact]
        public void GivenIsDateOnlyTrue_WhenEqualityPatternMatched_ThenCollapsedToSingleEndDateTimeEquality()
        {
            var expr = new SearchParameterExpression(BuildParam(isDateOnly: true), EqualityPattern());

            var result = expr.AcceptVisitor(DateOnlyEqualityRewriter.Instance, null);

            var rewritten = Assert.IsType<SearchParameterExpression>(result);
            var binary = Assert.IsType<BinaryExpression>(rewritten.Expression);
            Assert.Equal(FieldName.DateTimeEnd, binary.FieldName);
            Assert.Equal(BinaryOperator.Equal, binary.BinaryOperator);
            Assert.Equal(EndOfDay, binary.Value);
        }

        [Fact]
        public void GivenIsDateOnlyFalse_WhenEqualityPatternMatched_ThenPassThrough()
        {
            var expr = new SearchParameterExpression(BuildParam(isDateOnly: false), EqualityPattern());

            var result = expr.AcceptVisitor(DateOnlyEqualityRewriter.Instance, null);

            Assert.Same(expr, result);
        }

        [Fact]
        public void GivenIsDateOnlyTrue_WhenSingleSidedPredicate_ThenPassThrough()
        {
            var single = Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, StartOfDay);
            var expr = new SearchParameterExpression(BuildParam(isDateOnly: true), single);

            var result = expr.AcceptVisitor(DateOnlyEqualityRewriter.Instance, null);

            Assert.Same(expr, result);
        }

        [Fact]
        public void GivenIsDateOnlyTrue_WhenRangeOperatorPattern_ThenPassThrough()
        {
            // gt2016-07-06 emits a single-side StartDateTime > endOfDay; not the equality pattern.
            var range = Expression.GreaterThan(FieldName.DateTimeStart, null, EndOfDay);
            var expr = new SearchParameterExpression(BuildParam(isDateOnly: true), range);

            var result = expr.AcceptVisitor(DateOnlyEqualityRewriter.Instance, null);

            Assert.Same(expr, result);
        }

        [Fact]
        public void GivenCompositeParameter_WhenEqualityPatternMatched_ThenPassThrough()
        {
            // Composite parameters are out of scope for v1; the rewriter must not recurse into Component.
            var composite = new SearchParameterInfo(
                "Observation-code-value-date",
                "code-value-date",
                SearchParamType.Composite,
                new Uri("http://hl7.org/fhir/SearchParameter/Observation-code-value-date"),
                expression: "Observation",
                baseResourceTypes: new[] { "Observation" })
            {
                IsDateOnly = false,
            };
            var expr = new SearchParameterExpression(composite, EqualityPattern());

            var result = expr.AcceptVisitor(DateOnlyEqualityRewriter.Instance, null);

            Assert.Same(expr, result);
        }
    }
}
