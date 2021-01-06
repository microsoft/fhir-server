// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    public class SearchHelperUtilitiesTests
    {
        private readonly Uri _paramUri1 = new Uri("https://localhost/searchparam1");
        private readonly Uri _paramUri2 = new Uri("https://localhost/searchParam2");
        private readonly Uri _paramUri3 = new Uri("https://localhost/searchParam3");

        [Fact]
        public void GivenNullResourceSearchParameterStatus_WhenCalculateSearchParameterHash_ThenThrowsException()
        {
            Assert.ThrowsAny<Exception>(() => SearchHelperUtilities.CalculateSearchParameterHash(null));
        }

        [Fact]
        public void GivenEmptyResourceSearchParameterStatus_WhenCalculateSearchParameterHash_ThenThrowsException()
        {
            Assert.ThrowsAny<Exception>(() => SearchHelperUtilities.CalculateSearchParameterHash(new List<SearchParameterInfo>()));
        }

        [Fact]
        public void GivenTwoSameListsOfResourceSearchParameterStatus_WhenCalculateSearchParameterHash_ThenHashIsSame()
        {
            DateTimeOffset lastUpdated = DateTimeOffset.UtcNow;
            var param1 = GenerateSearchParameterInfo(_paramUri1, "patient");
            var param2 = GenerateSearchParameterInfo(_paramUri2, "observation");

            string hash1 = SearchHelperUtilities.CalculateSearchParameterHash(new List<SearchParameterInfo>() { param1, param2 });
            string hash2 = SearchHelperUtilities.CalculateSearchParameterHash(new List<SearchParameterInfo>() { param1, param2 });

            Assert.Equal(hash1, hash2);
        }

        [Fact]
        public void GivenTwoDifferentListsOfResourceSearchParameterStatus_WhenCalculateSearchParameterHash_ThenHashIsDifferent()
        {
            DateTimeOffset lastUpdated = DateTimeOffset.UtcNow;
            var param1 = GenerateSearchParameterInfo(_paramUri1, "patient");
            var param2 = GenerateSearchParameterInfo(_paramUri2, "observation");

            string hash1 = SearchHelperUtilities.CalculateSearchParameterHash(new List<SearchParameterInfo>() { param1, param2 });
            string hash2 = SearchHelperUtilities.CalculateSearchParameterHash(new List<SearchParameterInfo>() { param1 });

            Assert.NotEqual(hash1, hash2);
        }

        [Fact]
        public void GivenTwoSameListsDifferentOrderOfResourceSearchParameterStatus_WhenCalculateSearchParameterHash_ThenHashIsSame()
        {
            DateTimeOffset lastUpdated = DateTimeOffset.UtcNow;
            var param1 = GenerateSearchParameterInfo(_paramUri1, "patient");
            var param2 = GenerateSearchParameterInfo(_paramUri2, "observation");
            var param3 = GenerateSearchParameterInfo(_paramUri3, "medication");

            string hash1 = SearchHelperUtilities.CalculateSearchParameterHash(new List<SearchParameterInfo>() { param1, param2, param3 });
            string hash2 = SearchHelperUtilities.CalculateSearchParameterHash(new List<SearchParameterInfo>() { param2, param3, param1 });

            Assert.Equal(hash1, hash2);
        }

        private SearchParameterInfo GenerateSearchParameterInfo(Uri uri, string resourceType)
        {
            return new SearchParameterInfo(
                name: uri.Segments.LastOrDefault(),
                searchParamType: ValueSets.SearchParamType.String.ToString(),
                url: uri,
                components: null,
                expression: "expression",
                targetResourceTypes: null,
                baseResourceTypes: new List<string>() { resourceType });
        }
    }
}
