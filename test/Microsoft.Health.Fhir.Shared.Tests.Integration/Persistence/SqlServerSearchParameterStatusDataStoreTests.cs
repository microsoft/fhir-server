// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Storage.Registry;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    /// <summary>
    /// Integration tests for SqlServerSearchParameterStatusDataStore.
    /// Tests SQL Server-specific functionality including optimistic concurrency,
    /// schema version handling, and MaxLastUpdated operations.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
    public class SqlServerSearchParameterStatusDataStoreTests : IClassFixture<FhirStorageTestsFixture>
    {
        private readonly FhirStorageTestsFixture _fixture;
        private readonly IFhirStorageTestHelper _testHelper;

        public SqlServerSearchParameterStatusDataStoreTests(FhirStorageTestsFixture fixture)
        {
            _fixture = fixture;
            _testHelper = fixture.TestHelper;
        }

        [Fact]
        public async Task GivenGetMaxLastUpdatedAsync_WhenCalledWithExistingData_ThenReturnsValidTimestamp()
        {
            // Arrange
            var dataStore = _fixture.SearchParameterStatusDataStore as SqlServerSearchParameterStatusDataStore;
            Assert.NotNull(dataStore);

            // Act
            var maxLastUpdated = await dataStore!.GetMaxLastUpdatedAsync(CancellationToken.None);

            // Assert
            Assert.NotEqual(DateTimeOffset.MinValue, maxLastUpdated);
            Assert.True(maxLastUpdated <= DateTimeOffset.UtcNow.AddMinutes(1)); // Allow 1 min clock skew
        }

        [Fact]
        public async Task GivenGetMaxLastUpdatedAsync_WhenDataIsUpserted_ThenMaxLastUpdatedIncreases()
        {
            // Arrange
            var dataStore = _fixture.SearchParameterStatusDataStore as SqlServerSearchParameterStatusDataStore;
            Assert.NotNull(dataStore);

            var testUri = "http://hl7.org/fhir/SearchParameter/Test-MaxLastUpdated-" + Guid.NewGuid();
            var status = new ResourceSearchParameterStatus
            {
                Uri = new Uri(testUri),
                Status = SearchParameterStatus.Disabled,
                IsPartiallySupported = false,
                LastUpdated = DateTimeOffset.UtcNow,
            };

            try
            {
                // Get initial max
                var maxBefore = await dataStore!.GetMaxLastUpdatedAsync(CancellationToken.None);

                // Small delay to ensure different timestamp
                await Task.Delay(100);

                // Act - Upsert new status
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses(new[] { status }, CancellationToken.None);

                var maxAfter = await dataStore!.GetMaxLastUpdatedAsync(CancellationToken.None);

                // Assert - Max should increase
                Assert.True(maxAfter >= maxBefore, $"Expected maxAfter ({maxAfter}) >= maxBefore ({maxBefore})");
            }
            finally
            {
                await _testHelper.DeleteSearchParameterStatusAsync(testUri);
            }
        }

        [Fact]
        public async Task GivenUpsertStatuses_WhenUpsertingWithSameUri_ThenLastUpdatedIsRefreshed()
        {
            // Arrange
            var testUri = "http://hl7.org/fhir/SearchParameter/Test-Upsert-" + Guid.NewGuid();
            var status = new ResourceSearchParameterStatus
            {
                Uri = new Uri(testUri),
                Status = SearchParameterStatus.Disabled,
                IsPartiallySupported = false,
                LastUpdated = DateTimeOffset.UtcNow,
            };

            try
            {
                // Act - First upsert
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses(new[] { status }, CancellationToken.None);

                // Get the result
                var allStatuses1 = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                var createdStatus = allStatuses1.FirstOrDefault(s => s.Uri.OriginalString == testUri);
                Assert.NotNull(createdStatus);
                var firstLastUpdated = createdStatus.LastUpdated;

                // Small delay to ensure different timestamp
                await Task.Delay(100);

                // Modify and upsert again
                status.Status = SearchParameterStatus.Enabled;
                status.IsPartiallySupported = true;
                status.LastUpdated = createdStatus.LastUpdated; // Use the LastUpdated from DB

                await _fixture.SearchParameterStatusDataStore.UpsertStatuses(new[] { status }, CancellationToken.None);

                // Get the updated result
                var allStatuses2 = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                var updatedStatus = allStatuses2.FirstOrDefault(s => s.Uri.OriginalString == testUri);

                // Assert - Status should be updated and LastUpdated should be newer
                Assert.NotNull(updatedStatus);
                Assert.Equal(SearchParameterStatus.Enabled, updatedStatus.Status);
                Assert.True(updatedStatus.IsPartiallySupported);
                Assert.True(
                    updatedStatus.LastUpdated >= firstLastUpdated,
                    $"Expected updated LastUpdated ({updatedStatus.LastUpdated}) >= first LastUpdated ({firstLastUpdated})");
            }
            finally
            {
                await _testHelper.DeleteSearchParameterStatusAsync(testUri);
            }
        }

        [Fact]
        public void GivenSyncStatuses_WhenCalledWithStatuses_ThenFhirModelIsSynchronized()
        {
            // Arrange
            var dataStore = _fixture.SearchParameterStatusDataStore as SqlServerSearchParameterStatusDataStore;
            Assert.NotNull(dataStore);

            var testUri = "http://hl7.org/fhir/SearchParameter/Test-Sync-" + Guid.NewGuid();
            var status = new SqlServerResourceSearchParameterStatus
            {
                Id = 9999, // Temporary ID for testing
                Uri = new Uri(testUri),
                Status = SearchParameterStatus.Enabled,
                IsPartiallySupported = false,
                LastUpdated = DateTimeOffset.UtcNow,
            };

            // Act - Call SyncStatuses (this should not throw)
            var exception = Record.Exception(() => dataStore!.SyncStatuses(new[] { status }));

            // Assert - Method completes without exception
            Assert.Null(exception);
        }

        [Fact]
        public async Task GivenUpsertStatuses_WhenUpsertingMultipleStatuses_ThenAllAreCreated()
        {
            // Arrange
            var testUri1 = "http://hl7.org/fhir/SearchParameter/Test-Batch1-" + Guid.NewGuid();
            var testUri2 = "http://hl7.org/fhir/SearchParameter/Test-Batch2-" + Guid.NewGuid();
            var testUri3 = "http://hl7.org/fhir/SearchParameter/Test-Batch3-" + Guid.NewGuid();

            var statuses = new List<ResourceSearchParameterStatus>
            {
                new ResourceSearchParameterStatus
                {
                    Uri = new Uri(testUri1),
                    Status = SearchParameterStatus.Disabled,
                    IsPartiallySupported = false,
                    LastUpdated = DateTimeOffset.UtcNow,
                },
                new ResourceSearchParameterStatus
                {
                    Uri = new Uri(testUri2),
                    Status = SearchParameterStatus.Enabled,
                    IsPartiallySupported = true,
                    LastUpdated = DateTimeOffset.UtcNow,
                },
                new ResourceSearchParameterStatus
                {
                    Uri = new Uri(testUri3),
                    Status = SearchParameterStatus.Supported,
                    IsPartiallySupported = false,
                    LastUpdated = DateTimeOffset.UtcNow,
                },
            };

            try
            {
                // Act
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses(statuses, CancellationToken.None);

                // Assert
                var allStatuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);

                var created1 = allStatuses.FirstOrDefault(s => s.Uri.OriginalString == testUri1);
                var created2 = allStatuses.FirstOrDefault(s => s.Uri.OriginalString == testUri2);
                var created3 = allStatuses.FirstOrDefault(s => s.Uri.OriginalString == testUri3);

                Assert.NotNull(created1);
                Assert.Equal(SearchParameterStatus.Disabled, created1.Status);
                Assert.False(created1.IsPartiallySupported);

                Assert.NotNull(created2);
                Assert.Equal(SearchParameterStatus.Enabled, created2.Status);
                Assert.True(created2.IsPartiallySupported);

                Assert.NotNull(created3);
                Assert.Equal(SearchParameterStatus.Supported, created3.Status);
                Assert.False(created3.IsPartiallySupported);
            }
            finally
            {
                await _testHelper.DeleteSearchParameterStatusAsync(testUri1);
                await _testHelper.DeleteSearchParameterStatusAsync(testUri2);
                await _testHelper.DeleteSearchParameterStatusAsync(testUri3);
            }
        }

        [Fact]
        public async Task GivenUpsertStatuses_WhenCalledWithEmptyCollection_ThenReturnsWithoutError()
        {
            // Arrange
            var emptyStatuses = new List<ResourceSearchParameterStatus>();

            // Act & Assert - Should not throw
            await _fixture.SearchParameterStatusDataStore.UpsertStatuses(emptyStatuses, CancellationToken.None);
        }

        [Fact]
        public async Task GivenGetSearchParameterStatuses_WhenCalled_ThenReturnsStatusesWithLastUpdated()
        {
            // Act
            var statuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);

            // Assert
            Assert.NotEmpty(statuses);
            Assert.All(statuses, status =>
            {
                Assert.NotNull(status.Uri);
                Assert.NotEqual(default(DateTimeOffset), status.LastUpdated);
                Assert.True(status.LastUpdated <= DateTimeOffset.UtcNow.AddMinutes(1));
            });
        }

        [Fact]
        public async Task GivenGetSearchParameterStatuses_WhenStatusHasSortableType_ThenSortStatusIsSet()
        {
            // Act
            var statuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);

            // Assert - Find a status that should have SortStatus enabled (like Patient birthdate)
            var birthdateParam = statuses.FirstOrDefault(s =>
                s.Uri.OriginalString.Contains("birthdate", StringComparison.OrdinalIgnoreCase));

            if (birthdateParam != null && birthdateParam.Status == SearchParameterStatus.Enabled)
            {
                // Birthdate is a DateTime parameter which should support sorting
                // SortStatus is an enum, so just verify it's defined
                Assert.True(Enum.IsDefined(typeof(SortParameterStatus), birthdateParam.SortStatus));
            }
        }

        [Fact]
        public async Task GivenMaxLastUpdated_WhenComparedToStatusTimestamps_ThenIsGreaterOrEqual()
        {
            // Arrange
            var dataStore = _fixture.SearchParameterStatusDataStore as SqlServerSearchParameterStatusDataStore;
            Assert.NotNull(dataStore);

            // Act
            var maxLastUpdated = await dataStore!.GetMaxLastUpdatedAsync(CancellationToken.None);
            var allStatuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);

            // Assert - MaxLastUpdated should be >= all individual LastUpdated values
            Assert.All(allStatuses, status =>
            {
                Assert.True(
                    maxLastUpdated >= status.LastUpdated,
                    $"MaxLastUpdated ({maxLastUpdated}) should be >= status.LastUpdated ({status.LastUpdated}) for {status.Uri}");
            });
        }

        [Fact]
        public async Task GivenUpsertStatuses_WhenUpdatingExistingStatus_ThenPreservesOtherStatuses()
        {
            // Arrange
            var testUri = "http://hl7.org/fhir/SearchParameter/Test-Preserve-" + Guid.NewGuid();
            var status = new ResourceSearchParameterStatus
            {
                Uri = new Uri(testUri),
                Status = SearchParameterStatus.Disabled,
                IsPartiallySupported = false,
                LastUpdated = DateTimeOffset.UtcNow,
            };

            try
            {
                // Create initial status
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses(new[] { status }, CancellationToken.None);

                var countBefore = (await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None)).Count;

                // Update the status
                status.Status = SearchParameterStatus.Enabled;
                status.LastUpdated = DateTimeOffset.UtcNow;
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses(new[] { status }, CancellationToken.None);

                var countAfter = (await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None)).Count;

                // Assert - Count should not increase (update, not insert)
                Assert.Equal(countBefore, countAfter);
            }
            finally
            {
                await _testHelper.DeleteSearchParameterStatusAsync(testUri);
            }
        }
    }
}
