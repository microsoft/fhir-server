// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SearchResultTests
    {
        [Fact]
        public void GivenResults_WhenInitialized_ThenCorrectResultsShouldBeSet()
        {
            var expectedResourceWrapper = new SearchResultEntry[0];
            var expectedUnsupportedSearchParameters = new List<Tuple<string, string>>();

            var searchResult = new SearchResult(expectedResourceWrapper, null, null, expectedUnsupportedSearchParameters);

            Assert.Same(expectedResourceWrapper, searchResult.Results);
            Assert.Same(expectedUnsupportedSearchParameters, searchResult.UnsupportedSearchParameters);
        }
    }
}
