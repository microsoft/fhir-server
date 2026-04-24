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
    public class SinglePointSearchParameterRewriterTests
    {
        private static readonly DateTimeOffset StartOfDay = new DateTimeOffset(2016, 7, 6, 0, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset EndOfDay = new DateTimeOffset(2016, 7, 7, 0, 0, 0, TimeSpan.Zero); // Next day at midnight (exactly 24 hours)

        private static SearchParameterInfo BuildBirthDate() =>
            new SearchParameterInfo(
                "birthdate",
                "birthdate",
                SearchParamType.Date,
                new Uri("http://hl7.org/fhir/SearchParameter/individual-birthdate"),
                expression: "Patient.birthDate",
                baseResourceTypes: new[] { "Patient" });

        private static SearchParameterInfo BuildObservationDate() =>
            new SearchParameterInfo(
                "date",
                "date",
                SearchParamType.Date,
                new Uri("http://hl7.org/fhir/SearchParameter/Observation-date"),
                expression: "Observation.effective",
                baseResourceTypes: new[] { "Observation" });

        [Fact]
        public void GivenAllowlistedEqualityPattern_WhenRewritten_ThenPassesThroughUnchanged()
        {
            // Note: Equality rewrite is disabled because parser emits same shape for both equality and approximate.
            // Two-sided AND patterns now pass through unchanged until higher-layer metadata is available.
            var input = new SearchParameterExpression(
                BuildBirthDate(),
                Expression.And(
                    Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, StartOfDay),
                    Expression.LessThanOrEqual(FieldName.DateTimeEnd, null, EndOfDay)));

            var result = input.AcceptVisitor(SinglePointSearchParameterRewriter.Instance, null);

            // All two-sided AND patterns now pass through unchanged
            Assert.Same(input, result);
        }

        [Fact]
        public void GivenNonAllowlistedEqualityPattern_WhenRewritten_ThenPassThrough()
        {
            var input = new SearchParameterExpression(
                BuildObservationDate(),
                Expression.And(
                    Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, StartOfDay),
                    Expression.LessThanOrEqual(FieldName.DateTimeEnd, null, EndOfDay)));

            var result = input.AcceptVisitor(SinglePointSearchParameterRewriter.Instance, null);

            Assert.Same(input, result);
        }

        [Theory]
        [InlineData(BinaryOperator.GreaterThan, FieldName.DateTimeEnd)]
        [InlineData(BinaryOperator.GreaterThanOrEqual, FieldName.DateTimeEnd)]
        [InlineData(BinaryOperator.LessThan, FieldName.DateTimeStart)]
        [InlineData(BinaryOperator.LessThanOrEqual, FieldName.DateTimeStart)]
        public void GivenAllowlistedSingleColumnRangePredicate_WhenRewritten_ThenExpressionIsPreserved(BinaryOperator binaryOperator, FieldName fieldName)
        {
            object boundary = fieldName == FieldName.DateTimeEnd ? EndOfDay : StartOfDay;
            var input = new SearchParameterExpression(BuildBirthDate(), new BinaryExpression(binaryOperator, fieldName, null, boundary));

            var result = input.AcceptVisitor(SinglePointSearchParameterRewriter.Instance, null);

            Assert.Same(input, result);
        }

        [Fact]
        public void GivenApproximateShape_WhenRewritten_ThenPassThrough()
        {
            // Approximate patterns (±30 days) should NOT be rewritten.
            // They do not match the equality guard which checks for exact same-day boundaries.
            var approximateEnd = EndOfDay.AddDays(30);
            var input = new SearchParameterExpression(
                BuildBirthDate(),
                Expression.And(
                    Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, StartOfDay.AddDays(-30)),
                    Expression.LessThanOrEqual(FieldName.DateTimeEnd, null, approximateEnd)));

            var result = input.AcceptVisitor(SinglePointSearchParameterRewriter.Instance, null);

            // Should NOT be rewritten because it does not match the equality shape
            Assert.Same(input, result);
        }
    }
}
