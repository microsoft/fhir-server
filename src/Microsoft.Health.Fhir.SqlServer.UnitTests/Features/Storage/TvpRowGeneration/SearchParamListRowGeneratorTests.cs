// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage.TvpRowGeneration
{
    /// <summary>
    /// Unit tests for SearchParamListRowGenerator.
    /// Tests the row generation logic that converts ResourceSearchParameterStatus to SearchParamListRow,
    /// including LastUpdated timestamp handling.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SearchParamListRowGeneratorTests
    {
        private readonly SearchParamListRowGenerator _generator;

        public SearchParamListRowGeneratorTests()
        {
            _generator = new SearchParamListRowGenerator();
        }

        [Fact]
        public void GivenEmptyList_WhenGenerateRows_ThenReturnsEmptyCollection()
        {
            // Arrange
            var statuses = new List<ResourceSearchParameterStatus>();

            // Act
            var rows = _generator.GenerateRows(statuses).ToList();

            // Assert
            Assert.Empty(rows);
        }

        [Fact]
        public void GivenSingleStatusWithLastUpdated_WhenGenerateRows_ThenReturnsSingleRowWithAllFields()
        {
            // Arrange
            var lastUpdated = DateTimeOffset.UtcNow;
            var status = new ResourceSearchParameterStatus
            {
                Uri = new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"),
                Status = SearchParameterStatus.Enabled,
                IsPartiallySupported = false,
                LastUpdated = lastUpdated,
            };
            var statuses = new List<ResourceSearchParameterStatus> { status };

            // Act
            var rows = _generator.GenerateRows(statuses).ToList();

            // Assert
            Assert.Single(rows);
            Assert.Equal("http://hl7.org/fhir/SearchParameter/Patient-name", rows[0].Uri);
            Assert.Equal("Enabled", rows[0].Status);
            Assert.False(rows[0].IsPartiallySupported);
            Assert.Equal(lastUpdated, rows[0].LastUpdated);
        }

        [Fact]
        public void GivenStatusWithDefaultLastUpdated_WhenGenerateRows_ThenUsesDateTimeOffsetMinValue()
        {
            // Arrange - LastUpdated not set (defaults to default(DateTimeOffset))
            var status = new ResourceSearchParameterStatus
            {
                Uri = new Uri("http://hl7.org/fhir/SearchParameter/Patient-birthdate"),
                Status = SearchParameterStatus.Supported,
                IsPartiallySupported = true,
            };
            var statuses = new List<ResourceSearchParameterStatus> { status };

            // Act
            var rows = _generator.GenerateRows(statuses).ToList();

            // Assert - verify default LastUpdated handling
            Assert.Single(rows);
            Assert.Equal(DateTimeOffset.MinValue, rows[0].LastUpdated);
        }

        [Theory]
        [InlineData(SearchParameterStatus.Enabled, "Enabled")]
        [InlineData(SearchParameterStatus.Disabled, "Disabled")]
        [InlineData(SearchParameterStatus.Supported, "Supported")]
        [InlineData(SearchParameterStatus.PendingDisable, "PendingDisable")]
        [InlineData(SearchParameterStatus.PendingDelete, "PendingDelete")]
        public void GivenDifferentStatuses_WhenGenerateRows_ThenStatusIsMappedToString(SearchParameterStatus inputStatus, string expectedStatusString)
        {
            // Arrange
            var status = new ResourceSearchParameterStatus
            {
                Uri = new Uri("http://example.org/SearchParameter/test"),
                Status = inputStatus,
                IsPartiallySupported = false,
                LastUpdated = DateTimeOffset.UtcNow,
            };
            var statuses = new List<ResourceSearchParameterStatus> { status };

            // Act
            var rows = _generator.GenerateRows(statuses).ToList();

            // Assert
            Assert.Single(rows);
            Assert.Equal(expectedStatusString, rows[0].Status);
        }

        [Fact]
        public void GivenMultipleStatuses_WhenGenerateRows_ThenReturnsRowForEachStatusInOrder()
        {
            // Arrange
            var now = DateTimeOffset.UtcNow;
            var statuses = new List<ResourceSearchParameterStatus>
            {
                new ResourceSearchParameterStatus
                {
                    Uri = new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"),
                    Status = SearchParameterStatus.Enabled,
                    IsPartiallySupported = false,
                    LastUpdated = now.AddDays(-2),
                },
                new ResourceSearchParameterStatus
                {
                    Uri = new Uri("http://hl7.org/fhir/SearchParameter/Patient-birthdate"),
                    Status = SearchParameterStatus.Disabled,
                    IsPartiallySupported = true,
                    LastUpdated = now.AddDays(-1),
                },
                new ResourceSearchParameterStatus
                {
                    Uri = new Uri("http://hl7.org/fhir/SearchParameter/Observation-code"),
                    Status = SearchParameterStatus.Supported,
                    IsPartiallySupported = false,
                    LastUpdated = now,
                },
            };

            // Act
            var rows = _generator.GenerateRows(statuses).ToList();

            // Assert
            Assert.Equal(3, rows.Count);

            Assert.Equal("http://hl7.org/fhir/SearchParameter/Patient-name", rows[0].Uri);
            Assert.Equal("Enabled", rows[0].Status);
            Assert.False(rows[0].IsPartiallySupported);
            Assert.Equal(now.AddDays(-2), rows[0].LastUpdated);

            Assert.Equal("http://hl7.org/fhir/SearchParameter/Patient-birthdate", rows[1].Uri);
            Assert.Equal("Disabled", rows[1].Status);
            Assert.True(rows[1].IsPartiallySupported);
            Assert.Equal(now.AddDays(-1), rows[1].LastUpdated);

            Assert.Equal("http://hl7.org/fhir/SearchParameter/Observation-code", rows[2].Uri);
            Assert.Equal("Supported", rows[2].Status);
            Assert.False(rows[2].IsPartiallySupported);
            Assert.Equal(now, rows[2].LastUpdated);
        }
    }
}
