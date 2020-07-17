﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    public class SearchHelperUtilitiesTests
    {
        private readonly Uri _paramUri1 = new Uri("https://localhost/searchparam1");
        private readonly Uri _paramUri2 = new Uri("https://localhost/searchParam2");
        private readonly Uri _paramUri3 = new Uri("https://localhost/searchParam3");

        [Fact]
        public void GivenNullSearchParamInfo_WhenCalculateSearchParameterNameHash_ThenThrowsException()
        {
            Assert.ThrowsAny<Exception>(() => SearchHelperUtilities.CalculateSearchParameterNameHash(null));
        }

        [Fact]
        public void GivenEmptySearchParamInfo_WhenCalculateSearchParameterNameHash_ThenThrowsException()
        {
            Assert.ThrowsAny<Exception>(() => SearchHelperUtilities.CalculateSearchParameterNameHash(new List<ResourceSearchParameterStatus>()));
        }

        [Fact]
        public void GivenTwoSameListsOfResourceSearchParameterStatus_WhenCalculateSearchParameterNameHash_ThenHashIsSame()
        {
            DateTimeOffset lastUpdated = DateTimeOffset.UtcNow;
            var param1 = GenerateResourceSearchParameterStatus(_paramUri1, lastUpdated);
            var param2 = GenerateResourceSearchParameterStatus(_paramUri2, lastUpdated);

            string hash1 = SearchHelperUtilities.CalculateSearchParameterNameHash(new List<ResourceSearchParameterStatus>() { param1, param2 });
            string hash2 = SearchHelperUtilities.CalculateSearchParameterNameHash(new List<ResourceSearchParameterStatus>() { param1, param2 });

            Assert.Equal(hash1, hash2);
        }

        [Fact]
        public void GivenTwoDifferentListsOfResourceSearchParameterStatus_WhenCalculateSearchParameterNameHash_ThenHashIsDifferent()
        {
            DateTimeOffset lastUpdated = DateTimeOffset.UtcNow;
            var param1 = GenerateResourceSearchParameterStatus(_paramUri1, lastUpdated);
            var param2 = GenerateResourceSearchParameterStatus(_paramUri2, lastUpdated);

            string hash1 = SearchHelperUtilities.CalculateSearchParameterNameHash(new List<ResourceSearchParameterStatus>() { param1, param2 });
            string hash2 = SearchHelperUtilities.CalculateSearchParameterNameHash(new List<ResourceSearchParameterStatus>() { param1 });

            Assert.NotEqual(hash1, hash2);
        }

        [Fact]
        public void GivenTwoSameListsDifferentOrderOfResourceSearchParameterStatus_WhenCalculateSearchParameterNameHash_ThenHashIsSame()
        {
            DateTimeOffset lastUpdated = DateTimeOffset.UtcNow;
            var param1 = GenerateResourceSearchParameterStatus(_paramUri1, lastUpdated);
            var param2 = GenerateResourceSearchParameterStatus(_paramUri2, lastUpdated);
            var param3 = GenerateResourceSearchParameterStatus(_paramUri3, lastUpdated);

            string hash1 = SearchHelperUtilities.CalculateSearchParameterNameHash(new List<ResourceSearchParameterStatus>() { param1, param2, param3 });
            string hash2 = SearchHelperUtilities.CalculateSearchParameterNameHash(new List<ResourceSearchParameterStatus>() { param2, param3, param1 });

            Assert.Equal(hash1, hash2);
        }

        [Fact]
        public void GivenTwoSameListsDifferentLastUpdatedResourceSearchParameterStatus_WhenCalculateSearchParameterNameHash_ThenHashIsDifferent()
        {
            DateTimeOffset lastUpdated = DateTimeOffset.UtcNow.AddSeconds(-600);
            var param1 = GenerateResourceSearchParameterStatus(_paramUri1, lastUpdated);
            var param2 = GenerateResourceSearchParameterStatus(_paramUri2, lastUpdated);
            var latestParam1 = GenerateResourceSearchParameterStatus(_paramUri1, DateTimeOffset.UtcNow);

            string hash1 = SearchHelperUtilities.CalculateSearchParameterNameHash(new List<ResourceSearchParameterStatus>() { param1, param2 });
            string hash2 = SearchHelperUtilities.CalculateSearchParameterNameHash(new List<ResourceSearchParameterStatus>() { latestParam1, param2 });

            Assert.NotEqual(hash1, hash2);
        }

        private ResourceSearchParameterStatus GenerateResourceSearchParameterStatus(Uri uri, DateTimeOffset lastUpdated)
        {
            if (lastUpdated == default)
            {
                lastUpdated = DateTimeOffset.UtcNow;
            }

            return new ResourceSearchParameterStatus()
            {
                Uri = uri,
                Status = SearchParameterStatus.Enabled,
                IsPartiallySupported = false,
                LastUpdated = lastUpdated,
            };
        }
    }
}
