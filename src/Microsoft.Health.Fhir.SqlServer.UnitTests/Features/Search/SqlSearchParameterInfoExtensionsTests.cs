// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search
{
    /// <summary>
    /// Unit tests for SearchParameterInfoExtensions.
    /// Tests the ColumnLocation determination logic for different search parameter codes.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SqlSearchParameterInfoExtensionsTests
    {
        [Theory]
        [InlineData(SearchParameterNames.LastUpdated)]
        [InlineData(SqlSearchParameters.ResourceSurrogateIdParameterName)]
        [InlineData(SearchParameterNames.ResourceType)]
        [InlineData(SqlSearchParameters.PrimaryKeyParameterName)]
        public void GivenSpecialParameter_WhenColumnLocation_ThenReturnsBothResourceAndSearchParamTable(string paramCode)
        {
            // Arrange
            var searchParam = CreateSearchParameterInfo(paramCode);

            // Act
            var location = searchParam.ColumnLocation();

            // Assert - These special parameters are in both Resource table and search param tables
            Assert.True(location.HasFlag(SearchParameterColumnLocation.ResourceTable));
            Assert.True(location.HasFlag(SearchParameterColumnLocation.SearchParamTable));
        }

        [Fact]
        public void GivenIdParameter_WhenColumnLocation_ThenReturnsOnlyResourceTable()
        {
            // Arrange
            var searchParam = CreateSearchParameterInfo(SearchParameterNames.Id);

            // Act
            var location = searchParam.ColumnLocation();

            // Assert - _id is only in Resource table
            Assert.True(location.HasFlag(SearchParameterColumnLocation.ResourceTable));
            Assert.False(location.HasFlag(SearchParameterColumnLocation.SearchParamTable));
        }

        [Theory]
        [InlineData("name")]
        [InlineData("birthdate")]
        [InlineData("identifier")]
        [InlineData("code")]
        [InlineData("status")]
        public void GivenRegularSearchParameter_WhenColumnLocation_ThenReturnsOnlySearchParamTable(string paramCode)
        {
            // Arrange
            var searchParam = CreateSearchParameterInfo(paramCode);

            // Act
            var location = searchParam.ColumnLocation();

            // Assert - regular search parameters are only in search param tables
            Assert.False(location.HasFlag(SearchParameterColumnLocation.ResourceTable));
            Assert.True(location.HasFlag(SearchParameterColumnLocation.SearchParamTable));
        }

        [Fact]
        public void GivenCustomSearchParameter_WhenColumnLocation_ThenReturnsOnlySearchParamTable()
        {
            // Arrange
            var searchParam = CreateSearchParameterInfo("my-custom-param");

            // Act
            var location = searchParam.ColumnLocation();

            // Assert
            Assert.Equal(SearchParameterColumnLocation.SearchParamTable, location);
        }

        private static SearchParameterInfo CreateSearchParameterInfo(string code)
        {
            return new SearchParameterInfo(
                name: code,
                code: code,
                searchParamType: SearchParamType.String,
                url: new Uri($"http://test/{code}"),
                expression: null,
                targetResourceTypes: null,
                baseResourceTypes: null,
                description: null);
        }
    }
}
