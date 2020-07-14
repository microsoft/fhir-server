// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    public class SearchHelperUtilitiesTests
    {
        [Fact]
        public void GivenNullSearchParamInfo_WhenCalculateSearchParameterNameHash_ThenThrowsException()
        {
            Assert.ThrowsAny<Exception>(() => SearchHelperUtilities.CalculateSearchParameterNameHash(null));
        }

        [Fact]
        public void GivenEmptySearchParamInfo_WhenCalculateSearchParameterNameHash_ThenThrowsException()
        {
            Assert.ThrowsAny<Exception>(() => SearchHelperUtilities.CalculateSearchParameterNameHash(new List<SearchParameterInfo>()));
        }

        [Fact]
        public void GivenTwoSameListsOfSearchParamInfo_WhenCalculateSearchParameterNameHash_ThenHashIsSame()
        {
            IEnumerable<SearchParameterInfo> paramInfo1 = GenerateSearchParamInfo("_type", "supplement", "study");
            IEnumerable<SearchParameterInfo> paramInfo2 = GenerateSearchParamInfo("_type", "supplement", "study");

            string hash1 = SearchHelperUtilities.CalculateSearchParameterNameHash(paramInfo1);
            string hash2 = SearchHelperUtilities.CalculateSearchParameterNameHash(paramInfo2);

            Assert.Equal(hash1, hash2);
        }

        [Fact]
        public void GivenTwoDifferentListsOfSearchParamInfo_WhenCalculateSearchParameterNameHash_ThenHashIsNotSame()
        {
            IEnumerable<SearchParameterInfo> paramInfo1 = GenerateSearchParamInfo("_type", "supplement", "study");
            IEnumerable<SearchParameterInfo> paramInfo2 = GenerateSearchParamInfo("_type", "study");

            string hash1 = SearchHelperUtilities.CalculateSearchParameterNameHash(paramInfo1);
            string hash2 = SearchHelperUtilities.CalculateSearchParameterNameHash(paramInfo2);

            Assert.NotEqual(hash1, hash2);
        }

        [Fact]
        public void GivenTwoSameListsDifferentOrderOfSearchParamInfo_WhenCalculateSearchParameterNameHash_ThenHashIsSame()
        {
            IEnumerable<SearchParameterInfo> paramInfo1 = GenerateSearchParamInfo("_type", "supplement", "study");
            IEnumerable<SearchParameterInfo> paramInfo2 = GenerateSearchParamInfo("_type", "study", "supplement");

            string hash1 = SearchHelperUtilities.CalculateSearchParameterNameHash(paramInfo1);
            string hash2 = SearchHelperUtilities.CalculateSearchParameterNameHash(paramInfo2);

            Assert.Equal(hash1, hash2);
        }

        private List<SearchParameterInfo> GenerateSearchParamInfo(params string[] searchParamNames)
        {
            List<SearchParameterInfo> paramInfo = new List<SearchParameterInfo>();
            foreach (string paramName in searchParamNames)
            {
                paramInfo.Add(new SearchParameterInfo(paramName));
            }

            return paramInfo;
        }
    }
}
