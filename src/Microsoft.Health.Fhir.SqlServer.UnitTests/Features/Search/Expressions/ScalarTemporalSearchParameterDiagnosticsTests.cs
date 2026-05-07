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
    public class ScalarTemporalSearchParameterDiagnosticsTests
    {
        [Fact]
        public void GivenScalarTemporalEqualityNotAllowListed_WhenCollected_ThenCandidateIsReturned()
        {
            var parameter = new SearchParameterInfo(
                "test-date",
                "date",
                SearchParamType.Date,
                new Uri("http://example.org/SearchParameter/test-date"),
                expression: "MedicationRequest.authoredOn",
                baseResourceTypes: new[] { "MedicationRequest" })
            {
                IsScalarTemporal = true,
            };

            var expression = new SearchParameterExpression(
                parameter,
                Expression.And(
                    Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)),
                    Expression.LessThanOrEqual(FieldName.DateTimeEnd, null, new DateTimeOffset(2020, 12, 31, 23, 59, 59, TimeSpan.Zero).AddTicks(9999999))));

            var result = ScalarTemporalSearchParameterDiagnostics.Collect(expression);

            var candidate = Assert.Single(result);
            Assert.Equal("http://example.org/SearchParameter/test-date", candidate.Url);
            Assert.Equal("date", candidate.Code);
            Assert.True(candidate.IsScalarTemporal);
            Assert.False(candidate.IsAllowListed);
            Assert.True(candidate.HasEqualityShape);
            Assert.False(candidate.WouldRewrite);
        }

        [Fact]
        public void GivenAllowListedBirthdate_WhenCollected_ThenAllowListedIsTrue()
        {
            var parameter = new SearchParameterInfo(
                "birthdate",
                "birthdate",
                SearchParamType.Date,
                new Uri("http://hl7.org/fhir/SearchParameter/individual-birthdate"),
                expression: "Patient.birthDate",
                baseResourceTypes: new[] { "Patient" })
            {
                IsScalarTemporal = true,
            };

            var expression = new SearchParameterExpression(
                parameter,
                Expression.And(
                    Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)),
                    Expression.LessThanOrEqual(FieldName.DateTimeEnd, null, new DateTimeOffset(2020, 12, 31, 23, 59, 59, TimeSpan.Zero).AddTicks(9999999))));

            var result = ScalarTemporalSearchParameterDiagnostics.Collect(expression);

            var candidate = Assert.Single(result);
            Assert.True(candidate.IsAllowListed);
            Assert.True(candidate.WouldRewrite);
        }

        [Fact]
        public void GivenAllowListedBirthdateMonth_WhenCollected_ThenWouldRewriteIsFalse()
        {
            var parameter = new SearchParameterInfo(
                "birthdate",
                "birthdate",
                SearchParamType.Date,
                new Uri("http://hl7.org/fhir/SearchParameter/individual-birthdate"),
                expression: "Patient.birthDate",
                baseResourceTypes: new[] { "Patient" })
            {
                IsScalarTemporal = true,
            };

            var expression = new SearchParameterExpression(
                parameter,
                Expression.And(
                    Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, new DateTimeOffset(2020, 7, 1, 0, 0, 0, TimeSpan.Zero)),
                    Expression.LessThanOrEqual(FieldName.DateTimeEnd, null, new DateTimeOffset(2020, 7, 31, 23, 59, 59, TimeSpan.Zero).AddTicks(9999999))));

            var result = ScalarTemporalSearchParameterDiagnostics.Collect(expression);

            var candidate = Assert.Single(result);
            Assert.True(candidate.IsAllowListed);
            Assert.True(candidate.HasEqualityShape);
            Assert.False(candidate.WouldRewrite);
        }

        [Fact]
        public void GivenDiagnostics_WhenSummaryBuilt_ThenContainsOnlyParameterMetadata()
        {
            var parameter = new SearchParameterInfo(
                "birthdate",
                "birthdate",
                SearchParamType.Date,
                new Uri("http://hl7.org/fhir/SearchParameter/individual-birthdate"),
                expression: "Patient.birthDate",
                baseResourceTypes: new[] { "Patient" })
            {
                IsScalarTemporal = true,
            };

            var expression = new SearchParameterExpression(
                parameter,
                Expression.And(
                    Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)),
                    Expression.LessThanOrEqual(FieldName.DateTimeEnd, null, new DateTimeOffset(2020, 12, 31, 23, 59, 59, TimeSpan.Zero).AddTicks(9999999))));

            string summary = ScalarTemporalSearchParameterDiagnostics.BuildSummary(ScalarTemporalSearchParameterDiagnostics.Collect(expression));

            Assert.Equal("url=http://hl7.org/fhir/SearchParameter/individual-birthdate,code=birthdate,scalarTemporal=True,allowListed=True,equality=True,wouldRewrite=True", summary);
        }

        [Fact]
        public void GivenNoDiagnostics_WhenSummaryBuilt_ThenNoneIsReturned()
        {
            string summary = ScalarTemporalSearchParameterDiagnostics.BuildSummary(Array.Empty<ScalarTemporalSearchParameterDiagnostics.ScalarTemporalSearchParameterDiagnostic>());

            Assert.Equal("none", summary);
        }
    }
}
