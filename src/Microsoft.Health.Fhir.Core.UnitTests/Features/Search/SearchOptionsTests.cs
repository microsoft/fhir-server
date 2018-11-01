// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    public class SearchOptionsTests
    {
        private const string ParamNameResourceType = "resourceType";

        [Theory]
        [InlineData(5, 5)]
        [InlineData(99, 99)]
        [InlineData(100, 100)]
        [InlineData(105, 100)]
        public void GivenANumber_WhenMaxItemCountIsSet_ThenCorrectMaxItemCountShouldBeSet(int input, int expected)
        {
            SearchOptions searchOptions = new SearchOptions();

            searchOptions.MaxItemCount = input;

            Assert.Equal(expected, searchOptions.MaxItemCount);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(0)]
        public void GivenAnInvalidNumber_WhenMaxItemCountIsSet_ThenExceptionShouldBeThrown(int input)
        {
            SearchOptions searchOptions = new SearchOptions();

            Assert.Throws<InvalidOperationException>(() => searchOptions.MaxItemCount = input);
        }
    }
}
