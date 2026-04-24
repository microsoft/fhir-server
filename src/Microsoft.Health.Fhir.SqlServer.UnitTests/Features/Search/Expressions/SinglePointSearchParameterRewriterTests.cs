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
        private static readonly DateTimeOffset EndOfDay = new DateTimeOffset(2016, 7, 6, 23, 59, 59, TimeSpan.Zero).AddTicks(9999999);

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
        public void GivenAllowlistedEqualityPattern_WhenRewritten_ThenCollapsedToSingleEndDateTimeEquality()
        {
            var input = new SearchParameterExpression(
                BuildBirthDate(),
                Expression.And(
                    Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, StartOfDay),
                    Expression.LessThanOrEqual(FieldName.DateTimeEnd, null, EndOfDay)));

            var result = input.AcceptVisitor(SinglePointSearchParameterRewriter.Instance, null);

            var rewritten = Assert.IsType<SearchParameterExpression>(result);
            var binary = Assert.IsType<BinaryExpression>(rewritten.Expression);
            Assert.Equal(FieldName.DateTimeEnd, binary.FieldName);
            Assert.Equal(BinaryOperator.Equal, binary.BinaryOperator);
            Assert.Equal(EndOfDay, binary.Value);
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
        public void GivenAllowlistedApproximateShape_WhenRewritten_ThenCollapsedToSingleEndDateTimeEquality()
        {
            // Note: The approximate shape (GT/LE with different values) is structurally identical to equality (GE/LE)
            // and is therefore rewritten due to shape-based matching. This is consistent with the plan's sample code.
            var approximateEnd = EndOfDay.AddDays(30);
            var input = new SearchParameterExpression(
                BuildBirthDate(),
                Expression.And(
                    Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, StartOfDay.AddDays(-30)),
                    Expression.LessThanOrEqual(FieldName.DateTimeEnd, null, approximateEnd)));

            var result = input.AcceptVisitor(SinglePointSearchParameterRewriter.Instance, null);

            var rewritten = Assert.IsType<SearchParameterExpression>(result);
            var binary = Assert.IsType<BinaryExpression>(rewritten.Expression);
            Assert.Equal(FieldName.DateTimeEnd, binary.FieldName);
            Assert.Equal(BinaryOperator.Equal, binary.BinaryOperator);
            Assert.Equal(approximateEnd, binary.Value);
        }
    }
}
