// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions.Visitors
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SinglePointSearchParameterRewriterTests
    {
        private static readonly DateTimeOffset TestDate = new DateTimeOffset(2016, 7, 6, 0, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset EndOfDay = new DateTimeOffset(2016, 7, 7, 0, 0, 0, TimeSpan.Zero); // Next day at midnight (exactly 24 hours)

        [Fact]
        public void VisitSearchParameter_WithEqualityPattern_PassesThroughUnchanged()
        {
            // Arrange - Two-sided AND pattern: (DateTimeStart >= ...) AND (DateTimeEnd <= ...)
            // Note: Equality rewrite is disabled because parser emits same shape for both equality and approximate.
            var startExpression = Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, TestDate);
            var endExpression = Expression.LessThanOrEqual(FieldName.DateTimeEnd, null, EndOfDay);
            var expression = Expression.And(startExpression, endExpression);

            var searchParameter = new SearchParameterInfo(
                "birthdate",
                "birthdate",
                SearchParamType.Date,
                new Uri("http://hl7.org/fhir/SearchParameter/individual-birthdate"),
                expression: "Patient.birthDate",
                baseResourceTypes: new[] { "Patient" });

            var searchParamExpr = new SearchParameterExpression(searchParameter, expression);

            var rewriter = new SinglePointSearchParameterRewriter(
                new SinglePointSearchParameterRewritePolicy(new SinglePointSearchParameterRegistry()));

            // Act
            var result = rewriter.VisitSearchParameter(searchParamExpr, null);

            // Assert - Two-sided AND patterns pass through unchanged (no rewrite)
            Assert.Same(searchParamExpr, result);
        }

        [Fact]
        public void VisitSearchParameter_WithApproximatePattern_DoesNotRewrite()
        {
            // Arrange - Approximate pattern with ±30 days (not equality)
            var startApprox = TestDate.AddDays(-30);
            var endApprox = EndOfDay.AddDays(30);

            var startExpression = Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, startApprox);
            var endExpression = Expression.LessThanOrEqual(FieldName.DateTimeEnd, null, endApprox);
            var expression = Expression.And(startExpression, endExpression);

            var searchParameter = new SearchParameterInfo("date", "date", SearchParamType.Date, new Uri("http://hl7.org/fhir/SearchParameter/date"));

            var searchParamExpr = new SearchParameterExpression(searchParameter, expression);

            var rewriter = new SinglePointSearchParameterRewriter(
                new SinglePointSearchParameterRewritePolicy(new SinglePointSearchParameterRegistry()));

            // Act
            var result = rewriter.VisitSearchParameter(searchParamExpr, null);

            // Assert - Should NOT be rewritten; should pass through unchanged
            Assert.Same(searchParamExpr, result);
        }

        [Fact]
        public void VisitSearchParameter_WithReversedEqualityPattern_PassesThroughUnchanged()
        {
            // Arrange - Reversed two-sided AND: (DateTimeEnd <= ...) AND (DateTimeStart >= ...)
            // Note: Equality rewrite is disabled because parser emits same shape for both equality and approximate.
            var endExpression = Expression.LessThanOrEqual(FieldName.DateTimeEnd, null, EndOfDay);
            var startExpression = Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, TestDate);
            var expression = Expression.And(endExpression, startExpression);

            var searchParameter = new SearchParameterInfo(
                "birthdate",
                "birthdate",
                SearchParamType.Date,
                new Uri("http://hl7.org/fhir/SearchParameter/individual-birthdate"),
                expression: "Patient.birthDate",
                baseResourceTypes: new[] { "Patient" });

            var searchParamExpr = new SearchParameterExpression(searchParameter, expression);

            var rewriter = new SinglePointSearchParameterRewriter(
                new SinglePointSearchParameterRewritePolicy(new SinglePointSearchParameterRegistry()));

            // Act
            var result = rewriter.VisitSearchParameter(searchParamExpr, null);

            // Assert - Two-sided AND patterns pass through unchanged (no rewrite)
            Assert.Same(searchParamExpr, result);
        }

        [Fact]
        public void VisitSearchParameter_WithSingleColumnGTE_RewritesToSingleColumn()
        {
            // Arrange - Single-column predicate: DateTimeEnd >= ...
            var expression = Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, null, TestDate);

            var searchParameter = new SearchParameterInfo("date", "date", SearchParamType.Date, new Uri("http://hl7.org/fhir/SearchParameter/date"));

            var searchParamExpr = new SearchParameterExpression(searchParameter, expression);

            var rewriter = new SinglePointSearchParameterRewriter(
                new SinglePointSearchParameterRewritePolicy(new SinglePointSearchParameterRegistry()));

            // Act
            var result = rewriter.VisitSearchParameter(searchParamExpr, null);

            // Assert - Should be rewritten
            var resultExpr = Assert.IsType<SearchParameterExpression>(result);
            var binaryExpr = Assert.IsType<BinaryExpression>(resultExpr.Expression);
            Assert.Equal(FieldName.DateTimeEnd, binaryExpr.FieldName);
        }

        [Fact]
        public void VisitSearchParameter_WithNonDateParameter_DoesNotRewrite()
        {
            // Arrange - Non-date search parameter
            var expression = Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, null, TestDate);

            var searchParameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/name"));

            var searchParamExpr = new SearchParameterExpression(searchParameter, expression);

            var rewriter = new SinglePointSearchParameterRewriter(
                new SinglePointSearchParameterRewritePolicy(new SinglePointSearchParameterRegistry()));

            // Act
            var result = rewriter.VisitSearchParameter(searchParamExpr, null);

            // Assert - Should NOT be rewritten
            Assert.Same(searchParamExpr, result);
        }
    }
}
