// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    public class SearchResultTests
    {
        private const string ParamNameResults = "results";

        [Fact]
        public void GivenANullResult_WhenInitializing_ThenInitializationShouldFail()
        {
            Assert.Throws<ArgumentNullException>(ParamNameResults, () => new SearchResult(null, null));
        }

        [Fact]
        public void GivenResults_WhenInitialized_ThenCorrectResultsShouldBeSet()
        {
            var expected = new ResourceWrapper[0];

            SearchResult searchResult = new SearchResult(expected, null);

            Assert.Same(expected, searchResult.Results);
        }
    }
}
