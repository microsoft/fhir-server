// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SearchParameterInfoExtensionsTests
    {
        private readonly Uri _paramUri1 = new("https://localhost/searchparam1");
        private readonly Uri _paramUri2 = new("https://localhost/searchParam2");
        private readonly Uri _paramUri3 = new("https://localhost/searchParam3");

        [Fact]
        public void GivenNullResourceSearchParameterStatus_WhenCalculateSearchParameterHash_ThenThrowsException()
        {
            Assert.ThrowsAny<Exception>(() => ((IEnumerable<SearchParameterInfo>)null).CalculateSearchParameterHash());
        }

        [Fact]
        public void GivenEmptyResourceSearchParameterStatus_WhenCalculateSearchParameterHash_ThenThrowsException()
        {
            Assert.ThrowsAny<Exception>(() => new List<SearchParameterInfo>().CalculateSearchParameterHash());
        }

        [Fact]
        public void GivenSearchParamWithSortStatusEnabledOrSupported_WhenCalculateSearchParameterHash_ThenHashIsSame()
        {
            var paramEnabled = GenerateSearchParameterInfo(_paramUri1, "patient", SortParameterStatus.Enabled);
            var paramSupported = GenerateSearchParameterInfo(_paramUri1, "patient", SortParameterStatus.Supported);
            var paramDisabled = GenerateSearchParameterInfo(_paramUri1, "patient", SortParameterStatus.Disabled);

            string hash1 = new List<SearchParameterInfo> { paramEnabled }.CalculateSearchParameterHash();
            string hash2 = new List<SearchParameterInfo> { paramSupported }.CalculateSearchParameterHash();
            string hash3 = new List<SearchParameterInfo> { paramDisabled }.CalculateSearchParameterHash();

            Assert.Equal(hash1, hash2);
            Assert.NotEqual(hash1, hash3);
        }

        [Fact]
        public void GivenTwoSameListsOfResourceSearchParameterStatus_WhenCalculateSearchParameterHash_ThenHashIsSame()
        {
            DateTimeOffset lastUpdated = DateTimeOffset.UtcNow;
            var param1 = GenerateSearchParameterInfo(_paramUri1, "patient");
            var param2 = GenerateSearchParameterInfo(_paramUri2, "observation");

            string hash1 = new List<SearchParameterInfo>() { param1, param2 }.CalculateSearchParameterHash();
            string hash2 = new List<SearchParameterInfo>() { param1, param2 }.CalculateSearchParameterHash();

            Assert.Equal(hash1, hash2);
        }

        [Fact]
        public void GivenTwoDifferentListsOfResourceSearchParameterStatus_WhenCalculateSearchParameterHash_ThenHashIsDifferent()
        {
            DateTimeOffset lastUpdated = DateTimeOffset.UtcNow;
            var param1 = GenerateSearchParameterInfo(_paramUri1, "patient");
            var param2 = GenerateSearchParameterInfo(_paramUri2, "observation");

            string hash1 = new List<SearchParameterInfo>() { param1, param2 }.CalculateSearchParameterHash();
            string hash2 = new List<SearchParameterInfo>() { param1 }.CalculateSearchParameterHash();

            Assert.NotEqual(hash1, hash2);
        }

        [Fact]
        public void GivenTwoSameListsDifferentOrderOfResourceSearchParameterStatus_WhenCalculateSearchParameterHash_ThenHashIsSame()
        {
            DateTimeOffset lastUpdated = DateTimeOffset.UtcNow;
            var param1 = GenerateSearchParameterInfo(_paramUri1, "patient");
            var param2 = GenerateSearchParameterInfo(_paramUri2, "observation");
            var param3 = GenerateSearchParameterInfo(_paramUri3, "medication");

            string hash1 = new List<SearchParameterInfo>() { param1, param2, param3 }.CalculateSearchParameterHash();
            string hash2 = new List<SearchParameterInfo>() { param2, param3, param1 }.CalculateSearchParameterHash();

            Assert.Equal(hash1, hash2);
        }

        private SearchParameterInfo GenerateSearchParameterInfo(Uri uri, string resourceType, SortParameterStatus sortParameterStatus = SortParameterStatus.Disabled)
        {
            return new SearchParameterInfo(
                name: uri.Segments.LastOrDefault(),
                code: uri.Segments.LastOrDefault(),
                searchParamType: ValueSets.SearchParamType.String,
                url: uri,
                components: null,
                expression: "expression",
                targetResourceTypes: null,
                baseResourceTypes: new List<string> { resourceType })
            {
                SortStatus = sortParameterStatus,
            };
        }
    }
}
