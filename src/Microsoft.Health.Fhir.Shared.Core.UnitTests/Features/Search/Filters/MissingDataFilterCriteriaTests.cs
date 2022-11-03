// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Filters;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Search.Filters
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [Trait(Traits.Category, Categories.SmartOnFhir)]
    public sealed class MissingDataFilterCriteriaTests
    {
        [Theory]
        [InlineData(USCoreTestHelper.JsonCompliantDataSamplesFileName)]
        [InlineData(USCoreTestHelper.XmlCompliantDataSamplesFileName)]
        public void WhenApplyingFilteringCriteria_IfAllDataIsCompliant_ThenShowDataAsIs(string fileName)
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            // Evaluates if US Core Missing Data compliant records are returned as expected.

            const bool isCriteriaEnabled = true;
            const bool isSmartRequest = true;

            SearchResult searchResult = USCoreTestHelper.GetSearchResult(fileName);

            IFilterCriteria searchResultFilter = USCoreTestHelper.GetMissingDataFilterCriteria(isCriteriaEnabled, isSmartRequest);
            SearchResult filteredSearchResult = searchResultFilter.Apply(searchResult);

            Assert.Equal(searchResult.Results.Count(), filteredSearchResult.Results.Count());
            Assert.Equal(searchResult.SearchIssues.Count, filteredSearchResult.SearchIssues.Count);
            Assert.Empty(searchResult.SearchIssues);

            Assert.Equal(searchResult.ContinuationToken, filteredSearchResult.ContinuationToken);
            Assert.Equal(searchResult.SortOrder, filteredSearchResult.SortOrder);
        }

        [Theory]
        [InlineData(USCoreTestHelper.JsonNonCompliantDataSamplesFileName)]
        [InlineData(USCoreTestHelper.XmlNonCompliantDataSamplesFileName)]
        public void WhenApplyingFilteringCriteria_IfNoMissingStatusElements_ThenShowDataAsIs(string fileName)
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            // Evaluates if US Core Missing Data NOT compliant records are marked as searching issues.

            const bool isCriteriaEnabled = true;
            const bool isSmartRequest = true;

            SearchResult searchResult = USCoreTestHelper.GetSearchResult(fileName);

            IFilterCriteria searchResultFilter = USCoreTestHelper.GetMissingDataFilterCriteria(isCriteriaEnabled, isSmartRequest);
            SearchResult filteredSearchResult = searchResultFilter.Apply(searchResult);

            Assert.NotEqual(searchResult.Results.Count(), filteredSearchResult.Results.Count());
            Assert.NotEqual(searchResult.SearchIssues.Count, filteredSearchResult.SearchIssues.Count);
            Assert.Equal(searchResult.Results.Count(), filteredSearchResult.SearchIssues.Count);

            Assert.Equal(searchResult.ContinuationToken, filteredSearchResult.ContinuationToken);
            Assert.Equal(searchResult.SortOrder, filteredSearchResult.SortOrder);
        }
    }
}
