// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [Trait(Traits.Category, Categories.SmartOnFhir)]
    public sealed class DataResourceFilterTests
    {
        [SkippableTheory]
        [InlineData(true, true, USCoreTestHelper.JsonCompliantDataSamplesFileName)]
        [InlineData(false, true, USCoreTestHelper.JsonCompliantDataSamplesFileName)]
        [InlineData(true, false, USCoreTestHelper.JsonCompliantDataSamplesFileName)]
        [InlineData(false, false, USCoreTestHelper.JsonCompliantDataSamplesFileName)]
        [InlineData(true, true, USCoreTestHelper.XmlCompliantDataSamplesFileName)]
        [InlineData(false, true, USCoreTestHelper.XmlCompliantDataSamplesFileName)]
        [InlineData(true, false, USCoreTestHelper.XmlCompliantDataSamplesFileName)]
        [InlineData(false, false, USCoreTestHelper.XmlCompliantDataSamplesFileName)]
        public void WhenFilteringResults_IfNoMissingStatusElements_ThenShowDataAsIs(bool isUSCoreMissingDataEnabled, bool isSmartUserRequest, string fileName)
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            // This test evaluates if records compliant with US Core Missing Data are returned "as it's" under all combinations of configuration.

            SearchResult searchResult = USCoreTestHelper.GetSearchResult(fileName);

            IDataResourceFilter dataResourceFilter = USCoreTestHelper.GetDataResourceFilter(isUSCoreMissingDataEnabled, isSmartUserRequest);
            SearchResult filteredSearchResult = dataResourceFilter.Filter(searchResult);

            Assert.Equal(searchResult.Results.Count(), filteredSearchResult.Results.Count());
            Assert.Equal(searchResult.SearchIssues.Count, filteredSearchResult.SearchIssues.Count);
            Assert.Empty(filteredSearchResult.SearchIssues);

            Assert.Equal(searchResult.ContinuationToken, filteredSearchResult.ContinuationToken);
            Assert.Equal(searchResult.SortOrder, filteredSearchResult.SortOrder);

            // This part of the test evaluates if the bundle created from the filtered search result has the expected number of entries.

            IBundleFactory bundleFactory = USCoreTestHelper.GetBundleFactory(isSmartUserRequest);
            ResourceElement resourceElement = bundleFactory.CreateSearchBundle(filteredSearchResult);
            IReadOnlyList<Bundle.EntryComponent> entries = resourceElement.ToPoco<Bundle>().Entry;

            Assert.NotNull(entries);
            Assert.True(entries.Count == filteredSearchResult.Results.Count(), $"This test expects one entry for each record. Currently there are {entries.Count} operation outcomes for {filteredSearchResult.SearchIssues.Count} results.");
        }

        [SkippableTheory]
        [InlineData(true, USCoreTestHelper.JsonNonCompliantDataSamplesFileName)]
        [InlineData(false, USCoreTestHelper.JsonNonCompliantDataSamplesFileName)]
        [InlineData(true, USCoreTestHelper.XmlNonCompliantDataSamplesFileName)]
        [InlineData(false, USCoreTestHelper.XmlNonCompliantDataSamplesFileName)]
        public void WhenFilteringResults_IfMissingStatusElementsAndUSCoreIsDisable_ThenShowDataAsIs(bool isSmartUserRequest, string fileName)
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            // This test evaluates if records NON compliant with US Core Missing Data are returned "as it's" if US Core is disabled.

            const bool isUSCoreMissingDataEnabled = false;

            SearchResult searchResult = USCoreTestHelper.GetSearchResult(fileName);

            IDataResourceFilter dataResourceFilter = USCoreTestHelper.GetDataResourceFilter(isUSCoreMissingDataEnabled, isSmartUserRequest);
            SearchResult filteredSearchResult = dataResourceFilter.Filter(searchResult);

            Assert.Equal(searchResult.Results.Count(), filteredSearchResult.Results.Count());
            Assert.Equal(searchResult.SearchIssues.Count, filteredSearchResult.SearchIssues.Count);
            Assert.Empty(filteredSearchResult.SearchIssues); // As the test has US Core disabled, no search issues should be raised.

            Assert.Equal(searchResult.ContinuationToken, filteredSearchResult.ContinuationToken);
            Assert.Equal(searchResult.SortOrder, filteredSearchResult.SortOrder);

            // This part of the test evaluates if the bundle created from the filtered search result has the expected number of entries.

            IBundleFactory bundleFactory = USCoreTestHelper.GetBundleFactory(isSmartUserRequest);
            ResourceElement resourceElement = bundleFactory.CreateSearchBundle(filteredSearchResult);
            IReadOnlyList<Bundle.EntryComponent> entries = resourceElement.ToPoco<Bundle>().Entry;

            Assert.NotNull(entries);
            Assert.True(entries.Count == filteredSearchResult.Results.Count(), $"This test expects one entry for each record. Currently there are {entries.Count} operation outcomes for {filteredSearchResult.SearchIssues.Count} results.");
        }

        [SkippableTheory]
        [InlineData(true, USCoreTestHelper.JsonNonCompliantDataSamplesFileName)]
        [InlineData(false, USCoreTestHelper.JsonNonCompliantDataSamplesFileName)]
        [InlineData(true, USCoreTestHelper.XmlNonCompliantDataSamplesFileName)]
        [InlineData(false, USCoreTestHelper.XmlNonCompliantDataSamplesFileName)]
        public void WhenFilteringResults_IfMissingStatusElementsAndNotSmartUser_ThenShowDataAsIs(bool isUSCoreMissingDataEnabled, string fileName)
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            // This test evaluates if records NON compliant with US Core Missing Data are returned "as it's" if the request comes from a non-SMART user.

            const bool isSmartUserRequest = false;

            SearchResult searchResult = USCoreTestHelper.GetSearchResult(fileName);

            IDataResourceFilter dataResourceFilter = USCoreTestHelper.GetDataResourceFilter(isUSCoreMissingDataEnabled, isSmartUserRequest);
            SearchResult filteredSearchResult = dataResourceFilter.Filter(searchResult);

            Assert.Equal(searchResult.Results.Count(), filteredSearchResult.Results.Count());
            Assert.Equal(searchResult.SearchIssues.Count, filteredSearchResult.SearchIssues.Count);
            Assert.Empty(filteredSearchResult.SearchIssues); // As the test has no SMART user request, no search issues should be raised.

            Assert.Equal(searchResult.ContinuationToken, filteredSearchResult.ContinuationToken);
            Assert.Equal(searchResult.SortOrder, filteredSearchResult.SortOrder);

            // This part of the test evaluates if the bundle created from the filtered search result has the expected number of entries.

            IBundleFactory bundleFactory = USCoreTestHelper.GetBundleFactory(isSmartUserRequest);
            ResourceElement resourceElement = bundleFactory.CreateSearchBundle(filteredSearchResult);
            IReadOnlyList<Bundle.EntryComponent> entries = resourceElement.ToPoco<Bundle>().Entry;

            Assert.NotNull(entries);
            Assert.True(entries.Count == filteredSearchResult.Results.Count(), $"This test expects one entry for each record. Currently there are {entries.Count} operation outcomes for {filteredSearchResult.SearchIssues.Count} results.");
        }

        [SkippableTheory]
        [InlineData(USCoreTestHelper.JsonNonCompliantDataSamplesFileName)]
        [InlineData(USCoreTestHelper.XmlNonCompliantDataSamplesFileName)]
        public void WhenFilteringResults_IfMissingStatusElements_ThenReturnOperationOutcomeWith404(string fileName)
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            // This test evaluates if records NON compliant with US Core Missing Data are returned as searching issues.

            const bool isUSCoreMissingDataEnabled = true;
            const bool isSmartUserRequest = true;

            SearchResult searchResult = USCoreTestHelper.GetSearchResult(fileName);

            IDataResourceFilter dataResourceFilter = USCoreTestHelper.GetDataResourceFilter(isUSCoreMissingDataEnabled, isSmartUserRequest);
            SearchResult filteredSearchResult = dataResourceFilter.Filter(searchResult);

            Assert.NotEqual(searchResult.Results.Count(), filteredSearchResult.Results.Count());
            Assert.Empty(filteredSearchResult.Results); // This JSON file should only contain non-compliant samples.
            Assert.Equal(searchResult.Results.Count(), filteredSearchResult.SearchIssues.Count());
            Assert.NotEqual(searchResult.SearchIssues.Count, filteredSearchResult.SearchIssues.Count);
            Assert.NotEmpty(filteredSearchResult.SearchIssues);

            Assert.Equal(searchResult.ContinuationToken, filteredSearchResult.ContinuationToken);
            Assert.Equal(searchResult.SortOrder, filteredSearchResult.SortOrder);

            // This part of the test evaluates if the bundle created from the filtered search result has the expected number of entries.

            IBundleFactory bundleFactory = USCoreTestHelper.GetBundleFactory(isSmartUserRequest);
            ResourceElement resourceElement = bundleFactory.CreateSearchBundle(filteredSearchResult);
            IReadOnlyList<Bundle.EntryComponent> entries = resourceElement.ToPoco<Bundle>().Entry;

            Assert.NotNull(entries);
            Assert.True(entries.Count == 1, $"A single operation outcome is expected when there are searching issues. Currently there are {entries.Count} operation outcomes.");
        }

        [SkippableTheory]
        [InlineData(USCoreTestHelper.JsonNonCompliantDataSamplesFileName)]
        [InlineData(USCoreTestHelper.XmlNonCompliantDataSamplesFileName)]
        public void WhenFilteringResourceWrappers_IfMissingStatusElements_ThenReturnOperationOutcomeWith404(string fileName)
        {
            Skip.If(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B");

            // This test evaluates if records NON compliant with US Core Missing Data are returned as searching issues.

            const bool isUSCoreMissingDataEnabled = true;
            const bool isSmartUserRequest = true;

            SearchResult searchResult = USCoreTestHelper.GetSearchResult(fileName);

            IDataResourceFilter dataResourceFilter = USCoreTestHelper.GetDataResourceFilter(isUSCoreMissingDataEnabled, isSmartUserRequest);

            foreach (SearchResultEntry resultEntry in searchResult.Results)
            {
                FilterCriteriaOutcome outcome = dataResourceFilter.Match(resultEntry.Resource);

                Assert.False(outcome.Match);
                Assert.NotNull(outcome.OutcomeIssue);
            }
        }
    }
}
