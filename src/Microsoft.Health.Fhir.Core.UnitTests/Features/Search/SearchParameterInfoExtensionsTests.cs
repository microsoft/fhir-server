// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
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
            Assert.Equal(hash1, hash3);
        }

        [Fact]
        public void GivenTwoSameListsOfResourceSearchParameterStatus_WhenCalculateSearchParameterHash_ThenHashIsSame()
        {
            var param1 = GenerateSearchParameterInfo(_paramUri1, "patient");
            var param2 = GenerateSearchParameterInfo(_paramUri2, "observation");

            string hash1 = new List<SearchParameterInfo>() { param1, param2 }.CalculateSearchParameterHash();
            string hash2 = new List<SearchParameterInfo>() { param1, param2 }.CalculateSearchParameterHash();

            Assert.Equal(hash1, hash2);
        }

        [Fact]
        public void GivenTwoDifferentListsOfResourceSearchParameterStatus_WhenCalculateSearchParameterHash_ThenHashIsDifferent()
        {
            var param1 = GenerateSearchParameterInfo(_paramUri1, "patient");
            var param2 = GenerateSearchParameterInfo(_paramUri2, "observation");

            string hash1 = new List<SearchParameterInfo>() { param1, param2 }.CalculateSearchParameterHash();
            string hash2 = new List<SearchParameterInfo>() { param1 }.CalculateSearchParameterHash();

            Assert.NotEqual(hash1, hash2);
        }

        [Fact]
        public void GivenTwoSameListsDifferentOrderOfResourceSearchParameterStatus_WhenCalculateSearchParameterHash_ThenHashIsSame()
        {
            var param1 = GenerateSearchParameterInfo(_paramUri1, "patient");
            var param2 = GenerateSearchParameterInfo(_paramUri2, "observation");
            var param3 = GenerateSearchParameterInfo(_paramUri3, "medication");

            string hash1 = new List<SearchParameterInfo>() { param1, param2, param3 }.CalculateSearchParameterHash();
            string hash2 = new List<SearchParameterInfo>() { param2, param3, param1 }.CalculateSearchParameterHash();

            Assert.Equal(hash1, hash2);
        }

        [Fact]
        public void GivenSearchParamWithStatusEnabledOrSupported_WhenCalculateSearchParameterHash_ThenHashIsSame()
        {
            var param1 = GenerateSearchParameterInfo(_paramUri1, "patient", searchParameterStatus: SearchParameterStatus.Supported);
            var param2 = GenerateSearchParameterInfo(_paramUri1, "patient", searchParameterStatus: SearchParameterStatus.Enabled);

            string hash1 = new List<SearchParameterInfo>() { param1 }.CalculateSearchParameterHash();
            string hash2 = new List<SearchParameterInfo>() { param2 }.CalculateSearchParameterHash();

            Assert.Equal(hash1, hash2);
        }

        [Fact]
        public void GivenSearchParamWithStatusEnabledOrDeleted_WhenCalculateSearchParameterHash_ThenHashIsDifferent()
        {
            var param1 = GenerateSearchParameterInfo(_paramUri1, "patient", searchParameterStatus: SearchParameterStatus.Deleted);
            var param2 = GenerateSearchParameterInfo(_paramUri1, "patient", searchParameterStatus: SearchParameterStatus.Enabled);

            string hash1 = new List<SearchParameterInfo>() { param1 }.CalculateSearchParameterHash();
            string hash2 = new List<SearchParameterInfo>() { param2 }.CalculateSearchParameterHash();

            Assert.NotEqual(hash1, hash2);
        }

        [Fact]
        public void GivenSearchParamsWithStatusEnabledOrDeleted_WhenCalculateSearchParameterHash_ThenNotEnabledAreSkipped()
        {
            var param1 = GenerateSearchParameterInfo(_paramUri1, "patient", searchParameterStatus: SearchParameterStatus.Deleted);
            var param2 = GenerateSearchParameterInfo(_paramUri2, "observation", searchParameterStatus: SearchParameterStatus.Enabled);

            string hash1 = new List<SearchParameterInfo>() { param1, param2 }.CalculateSearchParameterHash();
            string hash2 = new List<SearchParameterInfo>() { param2 }.CalculateSearchParameterHash();

            Assert.Equal(hash1, hash2);
        }

        [Fact]
        public void GivenDifferentVectorConfiguration_WhenCalculateSearchParameterHash_ThenHashIsDifferent()
        {
            // Arrange
            var firstConfig = new VectorSearchParameterConfig { MaxInputTokens = 1000 };
            var secondConfig = new VectorSearchParameterConfig { MaxInputTokens = 2000 };
            SearchParameterInfo first = GenerateSearchParameterInfo(_paramUri1, "Observation", vectorConfig: firstConfig);
            SearchParameterInfo second = GenerateSearchParameterInfo(_paramUri1, "Observation", vectorConfig: secondConfig);

            // Act
            string firstHash = new[] { first }.CalculateSearchParameterHash();
            string secondHash = new[] { second }.CalculateSearchParameterHash();

            // Assert
            Assert.NotEqual(firstHash, secondHash);
        }

        [Theory]
        [InlineData(400, null, 800, null)]
        [InlineData(400, 40, 400, 80)]
        [InlineData(null, null, 400, null)]
        [InlineData(400, null, 400, 0)]
        [InlineData(4004, 0, 400, 40)]
        public void GivenDifferentVectorChunkSettings_WhenCalculateSearchParameterHash_ThenHashIsDifferent(
            int? firstChunkSizeTokens,
            int? firstChunkOverlapTokens,
            int? secondChunkSizeTokens,
            int? secondChunkOverlapTokens)
        {
            // Arrange
            var firstConfig = new VectorSearchParameterConfig { ChunkSizeTokens = firstChunkSizeTokens, ChunkOverlapTokens = firstChunkOverlapTokens };
            var secondConfig = new VectorSearchParameterConfig { ChunkSizeTokens = secondChunkSizeTokens, ChunkOverlapTokens = secondChunkOverlapTokens };
            SearchParameterInfo first = GenerateSearchParameterInfo(_paramUri1, "Observation", vectorConfig: firstConfig);
            SearchParameterInfo second = GenerateSearchParameterInfo(_paramUri1, "Observation", vectorConfig: secondConfig);

            // Act
            string firstHash = new[] { first }.CalculateSearchParameterHash();
            string secondHash = new[] { second }.CalculateSearchParameterHash();

            // Assert
            Assert.NotEqual(firstHash, secondHash);
        }

        [Fact]
        public void GivenSeparatelyBuiltIdenticalVectorConfigurations_WhenCalculateSearchParameterHash_ThenHashIsSame()
        {
            // Arrange
            var firstConfig = new VectorSearchParameterConfig { MinimumScore = 0.65m, ChunkSizeTokens = 400, ChunkOverlapTokens = 40 };
            var secondConfig = new VectorSearchParameterConfig { MinimumScore = 0.65m, ChunkSizeTokens = 400, ChunkOverlapTokens = 40 };
            SearchParameterInfo first = GenerateSearchParameterInfo(_paramUri1, "Observation", vectorConfig: firstConfig);
            SearchParameterInfo second = GenerateSearchParameterInfo(_paramUri1, "Observation", vectorConfig: secondConfig);

            // Act
            string firstHash = new[] { first }.CalculateSearchParameterHash();
            string secondHash = new[] { second }.CalculateSearchParameterHash();

            // Assert
            Assert.Equal(firstHash, secondHash);
        }

        [Fact]
        public void GivenSameVectorConfigurationUnderDifferentCultures_WhenCalculateSearchParameterHash_ThenHashIsSame()
        {
            // Arrange
            var config = new VectorSearchParameterConfig { MinimumScore = 0.65m, ChunkSizeTokens = 400, ChunkOverlapTokens = 40 };
            SearchParameterInfo searchParameter = GenerateSearchParameterInfo(_paramUri1, "Observation", vectorConfig: config);
            CultureInfo originalCulture = CultureInfo.CurrentCulture;
            string invariantHash;
            string frenchHash;

            try
            {
                // Act
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                invariantHash = new[] { searchParameter }.CalculateSearchParameterHash();
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                frenchHash = new[] { searchParameter }.CalculateSearchParameterHash();
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }

            // Assert
            Assert.Equal(invariantHash, frenchHash);
        }

        [Fact]
        public void GivenNonVectorSearchParameter_WhenCalculateSearchParameterHash_ThenExistingHashInputIsUnchanged()
        {
            // Arrange
            // Changing non-vector hash input would make every existing server report a reindex hash mismatch after upgrade.
            SearchParameterInfo searchParameter = GenerateSearchParameterInfo(_paramUri1, "Observation");
            string expectedHash = $"{_paramUri1}{ValueSets.SearchParamType.String}expressionObservation".ComputeHash();

            // Act
            string hash = new[] { searchParameter }.CalculateSearchParameterHash();

            // Assert
            Assert.Equal(expectedHash, hash);
        }

        private SearchParameterInfo GenerateSearchParameterInfo(
            Uri uri,
            string resourceType,
            SortParameterStatus sortParameterStatus = SortParameterStatus.Disabled,
            SearchParameterStatus searchParameterStatus = SearchParameterStatus.Enabled,
            VectorSearchParameterConfig vectorConfig = null)
        {
            return new SearchParameterInfo(
                name: uri.Segments.LastOrDefault(),
                code: uri.Segments.LastOrDefault(),
                searchParamType: ValueSets.SearchParamType.String,
                url: uri,
                components: null,
                expression: "expression",
                targetResourceTypes: null,
                baseResourceTypes: new List<string> { resourceType },
                vectorConfig: vectorConfig)
            {
                SearchParameterStatus = searchParameterStatus,
                SortStatus = sortParameterStatus,
            };
        }
    }
}
