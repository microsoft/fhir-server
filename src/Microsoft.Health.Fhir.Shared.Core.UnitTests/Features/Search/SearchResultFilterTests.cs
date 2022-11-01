// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [Trait(Traits.Category, Categories.SmartOnFhir)]
    public sealed class SearchResultFilterTests
    {
        [Theory]
        [InlineData(true, true, USCoreTestHelper.JsonCompliantDataSamplesFileName)]
        [InlineData(false, true, USCoreTestHelper.JsonCompliantDataSamplesFileName)]
        [InlineData(true, false, USCoreTestHelper.JsonCompliantDataSamplesFileName)]
        [InlineData(false, false, USCoreTestHelper.JsonCompliantDataSamplesFileName)]
        public void WhenFilteringResults_IfNoMissingStatusElements_ThenShowDataAsIs(bool isUSCoreMissingDataEnabled, bool isSmartUserRequest, string fileName)
        {
            // This test evaluates if records with all required status elements are returned "as it's" under all combinations of configuration.

            SearchResult searchResult = USCoreTestHelper.GetSearchResult(fileName);

            ISearchResultFilter searchResultFilter = USCoreTestHelper.GetSearchResultFilter(isUSCoreMissingDataEnabled, isSmartUserRequest);
            SearchResult filteredSearchResult = searchResultFilter.Filter(searchResult);

            Assert.Equal(searchResult.Results.Count(), filteredSearchResult.Results.Count());
            Assert.Equal(searchResult.SearchIssues.Count, filteredSearchResult.SearchIssues.Count);
            Assert.Empty(filteredSearchResult.SearchIssues);

            Assert.Equal(searchResult.ContinuationToken, filteredSearchResult.ContinuationToken);
            Assert.Equal(searchResult.SortOrder, filteredSearchResult.SortOrder);
        }

        [Theory]
        [InlineData(true, USCoreTestHelper.JsonNonCompliantDataSamplesFileName)]
        [InlineData(false, USCoreTestHelper.JsonNonCompliantDataSamplesFileName)]
        public void WhenFilteringResults_IfMissingStatusElementsAndUSCoreIsDisable_ThenShowDataAsIs(bool isSmartUserRequest, string fileName)
        {
            // This test evaluates if records missing required status elements return the data as "it's" if US Core is disabled.

            const bool isUSCoreMissingDataEnabled = false;

            SearchResult searchResult = USCoreTestHelper.GetSearchResult(fileName);

            ISearchResultFilter searchResultFilter = USCoreTestHelper.GetSearchResultFilter(isUSCoreMissingDataEnabled, isSmartUserRequest);
            SearchResult filteredSearchResult = searchResultFilter.Filter(searchResult);

            Assert.Equal(searchResult.Results.Count(), filteredSearchResult.Results.Count());
            Assert.Equal(searchResult.SearchIssues.Count, filteredSearchResult.SearchIssues.Count);
            Assert.Empty(filteredSearchResult.SearchIssues);

            Assert.Equal(searchResult.ContinuationToken, filteredSearchResult.ContinuationToken);
            Assert.Equal(searchResult.SortOrder, filteredSearchResult.SortOrder);
        }

        [Theory]
        [InlineData(true, USCoreTestHelper.JsonNonCompliantDataSamplesFileName)]
        [InlineData(false, USCoreTestHelper.JsonNonCompliantDataSamplesFileName)]
        public void WhenFilteringResults_IfMissingStatusElementsAndNotSmartUser_ThenShowDataAsIs(bool isUSCoreMissingDataEnabled, string fileName)
        {
            // This test evaluates if records missing required status elements return the data as "it's" if the request comes from a non-SMART user.

            const bool isSmartUserRequest = false;

            SearchResult searchResult = USCoreTestHelper.GetSearchResult(fileName);

            ISearchResultFilter searchResultFilter = USCoreTestHelper.GetSearchResultFilter(isUSCoreMissingDataEnabled, isSmartUserRequest);
            SearchResult filteredSearchResult = searchResultFilter.Filter(searchResult);

            Assert.Equal(searchResult.Results.Count(), filteredSearchResult.Results.Count());
            Assert.Equal(searchResult.SearchIssues.Count, filteredSearchResult.SearchIssues.Count);
            Assert.Empty(filteredSearchResult.SearchIssues);

            Assert.Equal(searchResult.ContinuationToken, filteredSearchResult.ContinuationToken);
            Assert.Equal(searchResult.SortOrder, filteredSearchResult.SortOrder);
        }

        [Theory]
        [InlineData(USCoreTestHelper.JsonNonCompliantDataSamplesFileName)]
        public void WhenFilteringResults_IfMissingStatusElements_ThenReturnOperationOutcomeWith404(string fileName)
        {
            const bool isUSCoreMissingDataEnabled = true;
            const bool isSmartUserRequest = true;

            SearchResult searchResult = USCoreTestHelper.GetSearchResult(fileName);

            ISearchResultFilter searchResultFilter = USCoreTestHelper.GetSearchResultFilter(isUSCoreMissingDataEnabled, isSmartUserRequest);
            SearchResult filteredSearchResult = searchResultFilter.Filter(searchResult);

            Assert.NotEqual(searchResult.Results.Count(), filteredSearchResult.Results.Count());
            Assert.Empty(filteredSearchResult.Results); // This JSON file should only contain non-compliant samples.
            Assert.Equal(searchResult.Results.Count(), filteredSearchResult.SearchIssues.Count());
            Assert.NotEqual(searchResult.SearchIssues.Count, filteredSearchResult.SearchIssues.Count);
            Assert.NotEmpty(filteredSearchResult.SearchIssues);

            Assert.Equal(searchResult.ContinuationToken, filteredSearchResult.ContinuationToken);
            Assert.Equal(searchResult.SortOrder, filteredSearchResult.SortOrder);
        }
    }
}
