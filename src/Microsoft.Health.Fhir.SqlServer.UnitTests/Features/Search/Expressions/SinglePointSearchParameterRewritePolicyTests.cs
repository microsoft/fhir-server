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
    /// <summary>
    /// Unit tests for SinglePointSearchParameterRewritePolicy.
    /// Tests the policy's decision logic for rewriting search expressions on single-point search parameters.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SinglePointSearchParameterRewritePolicyTests
    {
        private static readonly Uri BirthdateSearchParameterUrl = new Uri("http://hl7.org/fhir/SearchParameter/individual-birthdate");
        private static readonly Uri ObservationDateSearchParameterUrl = new Uri("http://hl7.org/fhir/SearchParameter/Observation-date");

        [Fact]
        public void GivenAllowlistedBirthdateWithEqualityOperator_WhenGettingRewriteDecision_ThenRewriteToEndDateTimeEquality()
        {
            // Arrange
            var searchParameterInfo = new SearchParameterInfo("birthdate", "birthdate", SearchParamType.Date, BirthdateSearchParameterUrl);

            // Act
            var decision = SinglePointSearchParameterRewritePolicy.GetRewriteDecision(searchParameterInfo, BinaryOperator.Equal);

            // Assert
            Assert.Equal(SinglePointRewriteDecision.RewriteToEndDateTimeEquality, decision);
        }

        [Fact]
        public void GivenAllowlistedBirthdateWithGreaterThanOperator_WhenGettingRewriteDecision_ThenUseExistingExpression()
        {
            // Arrange
            var searchParameterInfo = new SearchParameterInfo("birthdate", "birthdate", SearchParamType.Date, BirthdateSearchParameterUrl);

            // Act
            var decision = SinglePointSearchParameterRewritePolicy.GetRewriteDecision(searchParameterInfo, BinaryOperator.GreaterThan);

            // Assert
            Assert.Equal(SinglePointRewriteDecision.UseExistingExpression, decision);
        }

        [Fact]
        public void GivenAllowlistedBirthdateWithGreaterThanOrEqualOperator_WhenGettingRewriteDecision_ThenUseExistingExpression()
        {
            // Arrange
            var searchParameterInfo = new SearchParameterInfo("birthdate", "birthdate", SearchParamType.Date, BirthdateSearchParameterUrl);

            // Act
            var decision = SinglePointSearchParameterRewritePolicy.GetRewriteDecision(searchParameterInfo, BinaryOperator.GreaterThanOrEqual);

            // Assert
            Assert.Equal(SinglePointRewriteDecision.UseExistingExpression, decision);
        }

        [Fact]
        public void GivenAllowlistedBirthdateWithLessThanOperator_WhenGettingRewriteDecision_ThenUseExistingExpression()
        {
            // Arrange
            var searchParameterInfo = new SearchParameterInfo("birthdate", "birthdate", SearchParamType.Date, BirthdateSearchParameterUrl);

            // Act
            var decision = SinglePointSearchParameterRewritePolicy.GetRewriteDecision(searchParameterInfo, BinaryOperator.LessThan);

            // Assert
            Assert.Equal(SinglePointRewriteDecision.UseExistingExpression, decision);
        }

        [Fact]
        public void GivenAllowlistedBirthdateWithLessThanOrEqualOperator_WhenGettingRewriteDecision_ThenUseExistingExpression()
        {
            // Arrange
            var searchParameterInfo = new SearchParameterInfo("birthdate", "birthdate", SearchParamType.Date, BirthdateSearchParameterUrl);

            // Act
            var decision = SinglePointSearchParameterRewritePolicy.GetRewriteDecision(searchParameterInfo, BinaryOperator.LessThanOrEqual);

            // Assert
            Assert.Equal(SinglePointRewriteDecision.UseExistingExpression, decision);
        }

        [Fact]
        public void GivenAllowlistedBirthdateWithNotEqualOperator_WhenGettingRewriteDecision_ThenNoRewrite()
        {
            // Arrange
            var searchParameterInfo = new SearchParameterInfo("birthdate", "birthdate", SearchParamType.Date, BirthdateSearchParameterUrl);

            // Act
            var decision = SinglePointSearchParameterRewritePolicy.GetRewriteDecision(searchParameterInfo, BinaryOperator.NotEqual);

            // Assert
            Assert.Equal(SinglePointRewriteDecision.NoRewrite, decision);
        }

        [Fact]
        public void GivenNonAllowlistedObservationDateWithEqualityOperator_WhenGettingRewriteDecision_ThenNoRewrite()
        {
            // Arrange
            var searchParameterInfo = new SearchParameterInfo("date", "date", SearchParamType.Date, ObservationDateSearchParameterUrl);

            // Act
            var decision = SinglePointSearchParameterRewritePolicy.GetRewriteDecision(searchParameterInfo, BinaryOperator.Equal);

            // Assert
            Assert.Equal(SinglePointRewriteDecision.NoRewrite, decision);
        }

        [Fact]
        public void GivenNullSearchParameterInfo_WhenGettingRewriteDecision_ThenNoRewrite()
        {
            // Act
            var decision = SinglePointSearchParameterRewritePolicy.GetRewriteDecision(null, BinaryOperator.Equal);

            // Assert
            Assert.Equal(SinglePointRewriteDecision.NoRewrite, decision);
        }
    }
}
