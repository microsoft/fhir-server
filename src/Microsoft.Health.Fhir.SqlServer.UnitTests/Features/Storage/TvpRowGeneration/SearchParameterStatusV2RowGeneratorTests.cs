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
    /// Unit tests for SearchParameterStatusV2RowGenerator.
    /// Tests the row generation logic that converts ResourceSearchParameterStatus to SearchParamTableTypeV2Row.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SearchParameterStatusV2RowGeneratorTests
    {
        private readonly SearchParameterStatusV2RowGenerator _generator;

        public SearchParameterStatusV2RowGeneratorTests()
        {
            _generator = new SearchParameterStatusV2RowGenerator();
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
        public void GivenSingleStatus_WhenGenerateRows_ThenReturnsSingleRowWithCorrectValues()
        {
            // Arrange
            var status = new ResourceSearchParameterStatus
            {
                Uri = new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"),
                Status = SearchParameterStatus.Enabled,
                IsPartiallySupported = false,
            };
            var statuses = new List<ResourceSearchParameterStatus> { status };

            // Act
            var rows = _generator.GenerateRows(statuses).ToList();

            // Assert
            Assert.Single(rows);
            Assert.Equal("http://hl7.org/fhir/SearchParameter/Patient-name", rows[0].Uri);
            Assert.Equal("Enabled", rows[0].Status);
            Assert.False(rows[0].IsPartiallySupported);
        }

        [Fact]
        public void GivenStatusWithPartiallySupportedTrue_WhenGenerateRows_ThenRowReflectsPartiallySupported()
        {
            // Arrange
            var status = new ResourceSearchParameterStatus
            {
                Uri = new Uri("http://hl7.org/fhir/SearchParameter/Patient-birthdate"),
                Status = SearchParameterStatus.Supported,
                IsPartiallySupported = true,
            };
            var statuses = new List<ResourceSearchParameterStatus> { status };

            // Act
            var rows = _generator.GenerateRows(statuses).ToList();

            // Assert
            Assert.Single(rows);
            Assert.Equal("Supported", rows[0].Status);
            Assert.True(rows[0].IsPartiallySupported);
        }

        [Theory]
        [InlineData(SearchParameterStatus.Enabled, "Enabled")]
        [InlineData(SearchParameterStatus.Disabled, "Disabled")]
        [InlineData(SearchParameterStatus.Supported, "Supported")]
        [InlineData(SearchParameterStatus.PendingDisable, "PendingDisable")]
        [InlineData(SearchParameterStatus.PendingDelete, "PendingDelete")]
        public void GivenDifferentStatuses_WhenGenerateRows_ThenStatusIsMappedCorrectly(SearchParameterStatus inputStatus, string expectedStatusString)
        {
            // Arrange
            var status = new ResourceSearchParameterStatus
            {
                Uri = new Uri("http://example.org/SearchParameter/test"),
                Status = inputStatus,
                IsPartiallySupported = false,
            };
            var statuses = new List<ResourceSearchParameterStatus> { status };

            // Act
            var rows = _generator.GenerateRows(statuses).ToList();

            // Assert
            Assert.Single(rows);
            Assert.Equal(expectedStatusString, rows[0].Status);
        }

        [Fact]
        public void GivenMultipleStatuses_WhenGenerateRows_ThenReturnsRowForEachStatus()
        {
            // Arrange
            var statuses = new List<ResourceSearchParameterStatus>
            {
                new ResourceSearchParameterStatus
                {
                    Uri = new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"),
                    Status = SearchParameterStatus.Enabled,
                    IsPartiallySupported = false,
                },
                new ResourceSearchParameterStatus
                {
                    Uri = new Uri("http://hl7.org/fhir/SearchParameter/Patient-birthdate"),
                    Status = SearchParameterStatus.Disabled,
                    IsPartiallySupported = true,
                },
                new ResourceSearchParameterStatus
                {
                    Uri = new Uri("http://hl7.org/fhir/SearchParameter/Observation-code"),
                    Status = SearchParameterStatus.Supported,
                    IsPartiallySupported = false,
                },
            };

            // Act
            var rows = _generator.GenerateRows(statuses).ToList();

            // Assert
            Assert.Equal(3, rows.Count);

            Assert.Equal("http://hl7.org/fhir/SearchParameter/Patient-name", rows[0].Uri);
            Assert.Equal("Enabled", rows[0].Status);
            Assert.False(rows[0].IsPartiallySupported);

            Assert.Equal("http://hl7.org/fhir/SearchParameter/Patient-birthdate", rows[1].Uri);
            Assert.Equal("Disabled", rows[1].Status);
            Assert.True(rows[1].IsPartiallySupported);

            Assert.Equal("http://hl7.org/fhir/SearchParameter/Observation-code", rows[2].Uri);
            Assert.Equal("Supported", rows[2].Status);
            Assert.False(rows[2].IsPartiallySupported);
        }

        [Fact]
        public void GivenStatusWithRelativeUri_WhenGenerateRows_ThenOriginalStringIsUsed()
        {
            // Arrange - using a URI that might have different string representations
            var status = new ResourceSearchParameterStatus
            {
                Uri = new Uri("urn:uuid:12345678-1234-1234-1234-123456789012"),
                Status = SearchParameterStatus.Enabled,
                IsPartiallySupported = false,
            };
            var statuses = new List<ResourceSearchParameterStatus> { status };

            // Act
            var rows = _generator.GenerateRows(statuses).ToList();

            // Assert
            Assert.Single(rows);
            Assert.Equal("urn:uuid:12345678-1234-1234-1234-123456789012", rows[0].Uri);
        }
    }
}
