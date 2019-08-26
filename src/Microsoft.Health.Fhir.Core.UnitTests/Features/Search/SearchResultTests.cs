// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    public class SearchResultTests
    {
        [Fact]
        public void GivenResults_WhenInitialized_ThenCorrectResultsShouldBeSet()
        {
            var expectedResourceWrapper = new ResourceWrapper[0];
            var expectedUnsupportedSearchParameters = new List<Tuple<string, string>>();
            var expectedUnsupportedSortingParameters = new List<(string searchParameterName, string reason)>();

            var searchResult = new SearchResult(expectedResourceWrapper, expectedUnsupportedSearchParameters, expectedUnsupportedSortingParameters, null);

            Assert.Same(expectedResourceWrapper, searchResult.Results);
            Assert.Same(expectedUnsupportedSearchParameters, searchResult.UnsupportedSearchParameters);
            Assert.Same(expectedUnsupportedSortingParameters, searchResult.UnsupportedSortingParameters);
        }
    }
}
