// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Expression = Microsoft.Health.Fhir.Core.Features.Search.Expressions.Expression;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SqlSearchQueryComplexityCalculatorTests
    {
        private static readonly SearchParameterInfo IdParameter = SearchParameter(SearchParameterNames.Id, SearchParamType.Token);
        private static readonly SearchParameterInfo ResourceTypeParameter = SearchParameter(SearchParameterNames.ResourceType, SearchParamType.Token);
        private static readonly SearchParameterInfo BasedOnParameter = SearchParameter("based-on", SearchParamType.Reference, "ServiceRequest");
        private static readonly SearchParameterInfo ResultParameter = SearchParameter("result", SearchParamType.Reference, "Observation");
        private static readonly SearchParameterInfo PerformerParameter = SearchParameter("performer", SearchParamType.Reference, "PractitionerRole");
        private static readonly SearchParameterInfo PractitionerParameter = SearchParameter("practitioner", SearchParamType.Reference, "Practitioner");
        private static readonly SearchParameterInfo OrganizationParameter = SearchParameter("organization", SearchParamType.Reference, "Organization");
        private static readonly SearchParameterInfo SubjectParameter = SearchParameter("subject", SearchParamType.Reference, "Patient", "Group");
        private static readonly SearchParameterInfo NameParameter = SearchParameter("name", SearchParamType.String);

        public SqlSearchQueryComplexityCalculatorTests()
        {
            ModelInfoProvider.SetProvider(MockModelInfoProviderBuilder.Create(FhirSpecification.R4).Build());
        }

        [Fact]
        public void GivenResourceTypeAndIdSearch_WhenCalculated_ThenQueryIsStandard()
        {
            SearchOptions searchOptions = CreateSearchOptions(
                Expression.And(
                    ResourceType("Patient"),
                    Expression.SearchParameter(IdParameter, Expression.StringEquals(FieldName.TokenCode, null, "abc", false))));

            SqlSearchQueryComplexityResult result = SqlSearchQueryComplexityCalculator.Calculate(searchOptions);

            Assert.Equal(SqlSearchQueryComplexityTier.Standard, result.Tier);
            Assert.Equal(1, result.Score);
        }

        [Fact]
        public void GivenSingleNonIterativeInclude_WhenCalculated_ThenQueryIsComplex()
        {
            SearchOptions searchOptions = CreateSearchOptions(
                Expression.And(
                    ResourceType("ServiceRequest"),
                    Expression.Include(
                        new[] { "ServiceRequest" },
                        BasedOnParameter,
                        "DiagnosticReport",
                        "ServiceRequest",
                        new[] { "ServiceRequest" },
                        wildCard: false,
                        reversed: true,
                        iterate: false)));

            SqlSearchQueryComplexityResult result = SqlSearchQueryComplexityCalculator.Calculate(searchOptions);

            Assert.Equal(SqlSearchQueryComplexityTier.Complex, result.Tier);
            Assert.Equal(60, result.Score);
        }

        [Fact]
        public void GivenCustomerIterativeIncludeGraph_WhenCalculated_ThenQueryIsRejected()
        {
            SearchOptions searchOptions = CreateSearchOptions(
                Expression.And(
                    ResourceType("ServiceRequest"),
                    Expression.SearchParameter(BasedOnParameter, Expression.StringEquals(FieldName.ReferenceResourceId, null, "service-request-id", false)),
                    Expression.Include(new[] { "ServiceRequest" }, BasedOnParameter, "DiagnosticReport", "ServiceRequest", new[] { "ServiceRequest" }, wildCard: false, reversed: true, iterate: false),
                    Expression.Include(new[] { "DiagnosticReport" }, ResultParameter, "DiagnosticReport", "Observation", new[] { "Observation" }, wildCard: false, reversed: false, iterate: true),
                    Expression.Include(new[] { "Observation" }, PerformerParameter, "Observation", "PractitionerRole", new[] { "PractitionerRole" }, wildCard: false, reversed: false, iterate: true),
                    Expression.Include(new[] { "PractitionerRole" }, PractitionerParameter, "PractitionerRole", "Practitioner", new[] { "Practitioner" }, wildCard: false, reversed: false, iterate: true),
                    Expression.Include(new[] { "PractitionerRole" }, OrganizationParameter, "PractitionerRole", "Organization", new[] { "Organization" }, wildCard: false, reversed: false, iterate: true)),
                maxItemCount: 1000,
                includeCount: 1000);

            SqlSearchQueryComplexityResult result = SqlSearchQueryComplexityCalculator.Calculate(searchOptions);

            Assert.Equal(SqlSearchQueryComplexityTier.Rejected, result.Tier);
            Assert.Equal(583, result.Score);
        }

        [Fact]
        public void GivenWildcardIterativeInclude_WhenCalculated_ThenWildcardAndIterateArePenalized()
        {
            SearchOptions searchOptions = CreateSearchOptions(
                Expression.And(
                    ResourceType("Patient"),
                    Expression.Include(
                        new[] { "Patient" },
                        referenceSearchParameter: null,
                        sourceResourceType: "Patient",
                        targetResourceType: null,
                        referencedTypes: new[] { "Observation", "PractitionerRole" },
                        wildCard: true,
                        reversed: false,
                        iterate: true)));

            SqlSearchQueryComplexityResult result = SqlSearchQueryComplexityCalculator.Calculate(searchOptions);

            Assert.Equal(SqlSearchQueryComplexityTier.Expensive, result.Tier);
            Assert.Equal(200, result.Score);
        }

        [Fact]
        public void GivenAccurateTotalAndCustomSort_WhenCalculated_ThenQueryIsComplex()
        {
            SearchOptions searchOptions = CreateSearchOptions(
                Expression.And(
                    ResourceType("Patient"),
                    Expression.SearchParameter(NameParameter, Expression.Contains(FieldName.String, null, "smith", true))),
                includeTotal: TotalType.Accurate,
                sort: new[] { (NameParameter, SortOrder.Ascending) });

            SqlSearchQueryComplexityResult result = SqlSearchQueryComplexityCalculator.Calculate(searchOptions);

            Assert.Equal(SqlSearchQueryComplexityTier.Complex, result.Tier);
            Assert.Equal(65, result.Score);
        }

        [Fact]
        public void GivenNegatedResourceType_WhenCalculated_ThenQueryIsNotTreatedAsResourceConstrained()
        {
            SearchOptions searchOptions = CreateSearchOptions(Expression.Not(ResourceType("Patient")));

            SqlSearchQueryComplexityResult result = SqlSearchQueryComplexityCalculator.Calculate(searchOptions);

            Assert.Equal(SqlSearchQueryComplexityTier.Complex, result.Tier);
            Assert.Equal(53, result.Score);
        }

        [Fact]
        public void GivenOneLevelChain_WhenCalculated_ThenChainTraversalIsPenalized()
        {
            SearchOptions searchOptions = CreateSearchOptions(
                Expression.And(
                    ResourceType("Observation"),
                    Expression.Chained(
                        new[] { "Observation" },
                        SubjectParameter,
                        new[] { "Patient" },
                        reversed: false,
                        Expression.SearchParameter(NameParameter, Expression.Contains(FieldName.String, null, "smith", true)))));

            SqlSearchQueryComplexityResult result = SqlSearchQueryComplexityCalculator.Calculate(searchOptions);

            Assert.Equal(SqlSearchQueryComplexityTier.Complex, result.Tier);
            Assert.Equal(35, result.Score);
        }

        [Fact]
        public void GivenUntypedReferenceSearch_WhenCalculated_ThenUntypedReferenceIsPenalized()
        {
            SearchOptions searchOptions = CreateSearchOptions(
                Expression.And(
                    ResourceType("Observation"),
                    Expression.SearchParameter(SubjectParameter, Expression.StringEquals(FieldName.ReferenceResourceId, null, "123", false))));

            SqlSearchQueryComplexityResult result = SqlSearchQueryComplexityCalculator.Calculate(searchOptions);

            Assert.Equal(SqlSearchQueryComplexityTier.Standard, result.Tier);
            Assert.Equal(28, result.Score);
        }

        [Fact]
        public void GivenUntypedReferenceSearchWithNoDeclaredTargets_WhenCalculated_ThenUntypedReferenceIsPenalized()
        {
            SearchParameterInfo referenceParameter = new SearchParameterInfo("subject", "subject", SearchParamType.Reference);
            SearchOptions searchOptions = CreateSearchOptions(
                Expression.And(
                    ResourceType("Observation"),
                    Expression.SearchParameter(referenceParameter, Expression.StringEquals(FieldName.ReferenceResourceId, null, "123", false))));

            SqlSearchQueryComplexityResult result = SqlSearchQueryComplexityCalculator.Calculate(searchOptions);

            Assert.Equal(SqlSearchQueryComplexityTier.Standard, result.Tier);
            Assert.Equal(28, result.Score);
        }

        [Fact]
        public void GivenCountOnlySearchWithInclude_WhenCalculated_ThenIncludeCostIsIgnored()
        {
            SearchOptions searchOptions = CreateSearchOptions(
                Expression.And(
                    ResourceType("ServiceRequest"),
                    Expression.Include(new[] { "ServiceRequest" }, BasedOnParameter, "DiagnosticReport", "ServiceRequest", new[] { "ServiceRequest" }, wildCard: false, reversed: true, iterate: true)),
                countOnly: true);

            SqlSearchQueryComplexityResult result = SqlSearchQueryComplexityCalculator.Calculate(searchOptions);

            Assert.Equal(SqlSearchQueryComplexityTier.Standard, result.Tier);
            Assert.Equal(0, result.Score);
        }

        [Fact]
        public void GivenSystemWideSearch_WhenCalculated_ThenSystemWidePenaltyIsApplied()
        {
            SearchOptions searchOptions = CreateSearchOptions(expression: null);

            SqlSearchQueryComplexityResult result = SqlSearchQueryComplexityCalculator.Calculate(searchOptions);

            Assert.Equal(SqlSearchQueryComplexityTier.Complex, result.Tier);
            Assert.Equal(50, result.Score);
        }

        private static SearchOptions CreateSearchOptions(
            Expression expression,
            int maxItemCount = 10,
            int includeCount = 10,
            TotalType includeTotal = TotalType.None,
            (SearchParameterInfo SearchParameter, SortOrder SortOrder)[] sort = null,
            bool countOnly = false)
        {
            return new SearchOptions
            {
                Expression = expression,
                MaxItemCount = maxItemCount,
                IncludeCount = includeCount,
                IncludeTotal = includeTotal,
                CountOnly = countOnly,
                Sort = sort ?? Array.Empty<(SearchParameterInfo, SortOrder)>(),
                UnsupportedSearchParams = Array.Empty<Tuple<string, string>>(),
            };
        }

        private static Expression ResourceType(string resourceType)
        {
            return Expression.SearchParameter(ResourceTypeParameter, Expression.StringEquals(FieldName.TokenCode, null, resourceType, false));
        }

        private static SearchParameterInfo SearchParameter(string code, SearchParamType searchParamType, params string[] targetResourceTypes)
        {
            return new SearchParameterInfo(
                code,
                code,
                searchParamType,
                targetResourceTypes: targetResourceTypes);
        }
    }
}
