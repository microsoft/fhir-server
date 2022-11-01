// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Filters;
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
        [Fact]
        public void Xxx()
        {
            SearchResult searchResult = USCoreTestHelper.GetSearchResult(USCoreTestHelper.JsonNonCompliantDataSamplesFileName);

            IFilterCriteria searchResultFilter = new MissingDataFilterCriteria(isCriteriaEnabled: true, isSmartRequest: true);
            SearchResult filteredSearchResult = searchResultFilter.Apply(searchResult);

            Assert.Equal(searchResult.Results.Count(), filteredSearchResult.Results.Count());
            Assert.Equal(searchResult.SearchIssues.Count, filteredSearchResult.SearchIssues.Count);
            Assert.Empty(filteredSearchResult.SearchIssues);

            Assert.Equal(searchResult.ContinuationToken, filteredSearchResult.ContinuationToken);
            Assert.Equal(searchResult.SortOrder, filteredSearchResult.SortOrder);
        }
    }
}
