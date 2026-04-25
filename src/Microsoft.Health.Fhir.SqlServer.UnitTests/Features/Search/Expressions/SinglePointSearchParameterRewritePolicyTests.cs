// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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
        public void GivenAllowlistedBirthdateWithEqualityPattern_WhenDeciding_ThenRewriteToEndDateTimeEquality()
        {
            // Arrange
            var registry = new SinglePointSearchParameterRegistry();
            var policy = new SinglePointSearchParameterRewritePolicy(registry);
            var searchParameterInfo = new SearchParameterInfo("birthdate", "birthdate", SearchParamType.Date, BirthdateSearchParameterUrl);

            // Act
            var decision = policy.Decide(searchParameterInfo, SinglePointRewritePattern.Equality);

            // Assert
            Assert.Equal(SinglePointRewriteDecision.RewriteToEndDateTimeEquality, decision);
        }

        [Fact]
        public void GivenAllowlistedBirthdateWithGreaterThanPattern_WhenDeciding_ThenUseExistingExpression()
        {
            // Arrange
            var registry = new SinglePointSearchParameterRegistry();
            var policy = new SinglePointSearchParameterRewritePolicy(registry);
            var searchParameterInfo = new SearchParameterInfo("birthdate", "birthdate", SearchParamType.Date, BirthdateSearchParameterUrl);

            // Act
            var decision = policy.Decide(searchParameterInfo, SinglePointRewritePattern.GreaterThan);

            // Assert
            Assert.Equal(SinglePointRewriteDecision.UseExistingExpression, decision);
        }

        [Fact]
        public void GivenAllowlistedBirthdateWithGreaterThanOrEqualPattern_WhenDeciding_ThenUseExistingExpression()
        {
            // Arrange
            var registry = new SinglePointSearchParameterRegistry();
            var policy = new SinglePointSearchParameterRewritePolicy(registry);
            var searchParameterInfo = new SearchParameterInfo("birthdate", "birthdate", SearchParamType.Date, BirthdateSearchParameterUrl);

            // Act
            var decision = policy.Decide(searchParameterInfo, SinglePointRewritePattern.GreaterThanOrEqual);

            // Assert
            Assert.Equal(SinglePointRewriteDecision.UseExistingExpression, decision);
        }

        [Fact]
        public void GivenAllowlistedBirthdateWithLessThanPattern_WhenDeciding_ThenUseExistingExpression()
        {
            // Arrange
            var registry = new SinglePointSearchParameterRegistry();
            var policy = new SinglePointSearchParameterRewritePolicy(registry);
            var searchParameterInfo = new SearchParameterInfo("birthdate", "birthdate", SearchParamType.Date, BirthdateSearchParameterUrl);

            // Act
            var decision = policy.Decide(searchParameterInfo, SinglePointRewritePattern.LessThan);

            // Assert
            Assert.Equal(SinglePointRewriteDecision.UseExistingExpression, decision);
        }

        [Fact]
        public void GivenAllowlistedBirthdateWithLessThanOrEqualPattern_WhenDeciding_ThenUseExistingExpression()
        {
            // Arrange
            var registry = new SinglePointSearchParameterRegistry();
            var policy = new SinglePointSearchParameterRewritePolicy(registry);
            var searchParameterInfo = new SearchParameterInfo("birthdate", "birthdate", SearchParamType.Date, BirthdateSearchParameterUrl);

            // Act
            var decision = policy.Decide(searchParameterInfo, SinglePointRewritePattern.LessThanOrEqual);

            // Assert
            Assert.Equal(SinglePointRewriteDecision.UseExistingExpression, decision);
        }

        [Fact]
        public void GivenAllowlistedBirthdateWithUnsupportedPattern_WhenDeciding_ThenNoRewrite()
        {
            // Arrange
            var registry = new SinglePointSearchParameterRegistry();
            var policy = new SinglePointSearchParameterRewritePolicy(registry);
            var searchParameterInfo = new SearchParameterInfo("birthdate", "birthdate", SearchParamType.Date, BirthdateSearchParameterUrl);

            // Act
            var decision = policy.Decide(searchParameterInfo, SinglePointRewritePattern.Unsupported);

            // Assert
            Assert.Equal(SinglePointRewriteDecision.NoRewrite, decision);
        }

        [Fact]
        public void GivenNonAllowlistedObservationDateWithEqualityPattern_WhenDeciding_ThenNoRewrite()
        {
            // Arrange
            var registry = new SinglePointSearchParameterRegistry();
            var policy = new SinglePointSearchParameterRewritePolicy(registry);
            var searchParameterInfo = new SearchParameterInfo("date", "date", SearchParamType.Date, ObservationDateSearchParameterUrl);

            // Act
            var decision = policy.Decide(searchParameterInfo, SinglePointRewritePattern.Equality);

            // Assert
            Assert.Equal(SinglePointRewriteDecision.NoRewrite, decision);
        }

        [Fact]
        public void GivenNullSearchParameterInfo_WhenDeciding_ThenNoRewrite()
        {
            // Arrange
            var registry = new SinglePointSearchParameterRegistry();
            var policy = new SinglePointSearchParameterRewritePolicy(registry);

            // Act
            var decision = policy.Decide(null, SinglePointRewritePattern.Equality);

            // Assert
            Assert.Equal(SinglePointRewriteDecision.NoRewrite, decision);
        }
    }
}
