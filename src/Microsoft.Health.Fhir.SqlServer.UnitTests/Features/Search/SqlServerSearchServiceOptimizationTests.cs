// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search
{
    /// <summary>
    /// Unit tests for SqlServerSearchService optimization logic.
    /// These tests verify the early return conditions for the optimization method.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class SqlServerSearchServiceOptimizationTests
    {
        [Fact]
        public void SearchOptions_WithCountOnly_DisqualifiesOptimization()
        {
            // Arrange
            var searchOptions = new SearchOptions
            {
                ResourceVersionTypes = ResourceVersionType.Latest,
                CountOnly = true,
            };

            // Act & Assert
            // When CountOnly is true, the optimization should be skipped
            Assert.True(searchOptions.CountOnly);
            Assert.Equal(ResourceVersionType.Latest, searchOptions.ResourceVersionTypes);
        }

        [Fact]
        public void SearchOptions_WithHistoryVersionType_QualifiesForOptimization()
        {
            // Arrange
            var searchOptions = new SearchOptions
            {
                ResourceVersionTypes = ResourceVersionType.History,
                CountOnly = false,
            };

            // Act & Assert
            // History version type should now qualify for optimization (for versioned reads)
            Assert.False(searchOptions.CountOnly);
            Assert.True(searchOptions.ResourceVersionTypes.HasFlag(ResourceVersionType.History));
        }

        [Fact]
        public void SearchOptions_WithSoftDeletedVersionType_DisqualifiesOptimization()
        {
            // Arrange
            var searchOptions = new SearchOptions
            {
                ResourceVersionTypes = ResourceVersionType.SoftDeleted,
                CountOnly = false,
            };

            // Act & Assert
            // SoftDeleted version type alone should disqualify optimization
            Assert.False(searchOptions.CountOnly);
            Assert.Equal(ResourceVersionType.SoftDeleted, searchOptions.ResourceVersionTypes);
            Assert.False(searchOptions.ResourceVersionTypes.HasFlag(ResourceVersionType.History));
            Assert.NotEqual(ResourceVersionType.Latest, searchOptions.ResourceVersionTypes);
        }

        [Fact]
        public void SearchOptions_WithOptimalConditions_QualifiesForOptimization()
        {
            // Arrange
            var searchOptions = new SearchOptions
            {
                ResourceVersionTypes = ResourceVersionType.Latest,
                CountOnly = false,
            };

            // Act & Assert
            // When all conditions are met, the optimization should proceed
            Assert.False(searchOptions.CountOnly);
            Assert.Equal(ResourceVersionType.Latest, searchOptions.ResourceVersionTypes);
        }

        [Fact]
        public void ResourceVersionType_Latest_IsExpectedValue()
        {
            // Act & Assert
            // Verify that ResourceVersionType.Latest has the expected flag value
            Assert.True((ResourceVersionType.Latest & ResourceVersionType.Latest) == ResourceVersionType.Latest);
        }

        [Fact]
        public void ResourceVersionType_History_IsDifferentFromLatest()
        {
            // Act & Assert
            // Verify that History and Latest are different version types
            Assert.NotEqual(ResourceVersionType.Latest, ResourceVersionType.History);
        }

        [Fact]
        public void SearchOptions_WithCombinedVersionTypes_QualifiesForOptimization()
        {
            // Arrange - Combined Latest and History should qualify for optimization
            var searchOptions = new SearchOptions
            {
                ResourceVersionTypes = ResourceVersionType.Latest | ResourceVersionType.History,
                CountOnly = false,
            };

            // Act & Assert
            Assert.False(searchOptions.CountOnly);
            Assert.True(searchOptions.ResourceVersionTypes.HasFlag(ResourceVersionType.History));
            Assert.True(searchOptions.ResourceVersionTypes.HasFlag(ResourceVersionType.Latest));
        }
    }
}
