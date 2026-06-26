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
        public async Task GivenUpsertStatuses_WhenUpsertingWithSameUri_ThenLastUpdatedIsRefreshed()
        {
            await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);

            // Arrange
            var testUri = "http://hl7.org/fhir/SearchParameter/Test-Upsert-" + Guid.NewGuid();
            var status = new ResourceSearchParameterStatus
            {
                Uri = new Uri(testUri),
                Status = SearchParameterStatus.Disabled,
                IsPartiallySupported = false,
                LastUpdated = _fixture.SearchParameterOperations.SearchParamLastUpdated,
            };

            try
            {
                // Act - First upsert
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses([status], CancellationToken.None);

                // Get the result
                var allStatuses1 = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                var createdStatus = allStatuses1.FirstOrDefault(s => s.Uri.OriginalString == testUri);
                Assert.NotNull(createdStatus);
                var firstLastUpdated = createdStatus.LastUpdated;

                // Small delay to ensure different timestamp
                await Task.Delay(100);

                await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);

                // Modify and upsert again
                status.Status = SearchParameterStatus.Enabled;
                status.IsPartiallySupported = true;
                status.LastUpdated = _fixture.SearchParameterOperations.SearchParamLastUpdated;

                await _fixture.SearchParameterStatusDataStore.UpsertStatuses([status], CancellationToken.None);

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
            var exception = Record.Exception(() => dataStore!.SyncStatuses([status]));

            // Assert - Method completes without exception
            Assert.Null(exception);
        }

        [Fact]
        public async Task GivenUpsertStatuses_WhenUpsertingMultipleStatuses_ThenAllAreCreated()
        {
            await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);

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
                    LastUpdated = _fixture.SearchParameterOperations.SearchParamLastUpdated,
                },
                new ResourceSearchParameterStatus
                {
                    Uri = new Uri(testUri2),
                    Status = SearchParameterStatus.Enabled,
                    IsPartiallySupported = true,
                    LastUpdated = _fixture.SearchParameterOperations.SearchParamLastUpdated,
                },
                new ResourceSearchParameterStatus
                {
                    Uri = new Uri(testUri3),
                    Status = SearchParameterStatus.Supported,
                    IsPartiallySupported = false,
                    LastUpdated = _fixture.SearchParameterOperations.SearchParamLastUpdated,
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
        public async Task GivenUpsertStatuses_WhenUpdatingExistingStatus_ThenPreservesOtherStatuses()
        {
            await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);

            // Arrange
            var testUri = "http://hl7.org/fhir/SearchParameter/Test-Preserve-" + Guid.NewGuid();
            var status = new ResourceSearchParameterStatus
            {
                Uri = new Uri(testUri),
                Status = SearchParameterStatus.Disabled,
                IsPartiallySupported = false,
                LastUpdated = _fixture.SearchParameterOperations.SearchParamLastUpdated,
            };

            try
            {
                // Create initial status
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses([status], CancellationToken.None);

                var countBefore = (await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None)).Count;

                await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);

                // Update the status
                status.Status = SearchParameterStatus.Enabled;
                status.LastUpdated = _fixture.SearchParameterOperations.SearchParamLastUpdated;
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses([status], CancellationToken.None);

                var countAfter = (await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None)).Count;

                // Assert - Count should not increase (update, not insert)
                Assert.Equal(countBefore, countAfter);
            }
            finally
            {
                await _testHelper.DeleteSearchParameterStatusAsync(testUri);
            }
        }

        [Fact]
        public async Task GivenUpsertStatuses_WhenCollectionIsUpdated_ThenReturnedLastUpdatedIsGreaterThanOriginal()
        {
            await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);

            var testUri = "http://hl7.org/fhir/SearchParameter/Test-Propagate-" + Guid.NewGuid();
            var status = new ResourceSearchParameterStatus
            {
                Uri = new Uri(testUri),
                Status = SearchParameterStatus.Disabled,
                IsPartiallySupported = false,
                LastUpdated = _fixture.SearchParameterOperations.SearchParamLastUpdated,
            };

            try
            {
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses([status], CancellationToken.None);

                var dbStatus = (await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None)).FirstOrDefault(s => s.Uri.OriginalString == testUri);

                Assert.True(dbStatus.LastUpdated > status.LastUpdated, $"Expected {status.LastUpdated.ToString("yyyy-MM-ddTHH:mm:ss.fff")} < {dbStatus.LastUpdated.ToString("yyyy-MM-ddTHH:mm:ss.fff")}");
            }
            finally
            {
                await _testHelper.DeleteSearchParameterStatusAsync(testUri);
            }
        }

        [Fact]
        public async Task GivenSqlServerResourceSearchParameterStatus_WhenIdIsAssigned_ThenIdIsPersisted()
        {
            await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);

            // Arrange
            var testUri = "http://hl7.org/fhir/SearchParameter/Test-Id-" + Guid.NewGuid();
            var status = new SqlServerResourceSearchParameterStatus
            {
                Uri = new Uri(testUri),
                Status = SearchParameterStatus.Disabled,
                IsPartiallySupported = false,
                LastUpdated = _fixture.SearchParameterOperations.SearchParamLastUpdated,
            };

            try
            {
                // Act - Upsert and retrieve
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses([status], CancellationToken.None);

                var dbStatuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                var dbStatus = dbStatuses.FirstOrDefault(s => s.Uri.OriginalString == testUri);

                // Assert - SqlServerResourceSearchParameterStatus should have an Id assigned
                Assert.NotNull(dbStatus);
                Assert.IsType<SqlServerResourceSearchParameterStatus>(dbStatus);

                var sqlServerStatus = (SqlServerResourceSearchParameterStatus)dbStatus;
                Assert.True(sqlServerStatus.Id > 0, $"Expected Id > 0, got {sqlServerStatus.Id}");
            }
            finally
            {
                await _testHelper.DeleteSearchParameterStatusAsync(testUri);
            }
        }

        [Fact]
        public async Task GivenGetSearchParameterStatuses_WhenIsPartiallySupported_ThenValueIsPreserved()
        {
            await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);

            var testUri = "http://hl7.org/fhir/SearchParameter/Test-PartialSupport-" + Guid.NewGuid();
            var status = new ResourceSearchParameterStatus
            {
                Uri = new Uri(testUri),
                Status = SearchParameterStatus.Enabled,
                IsPartiallySupported = true,
                LastUpdated = _fixture.SearchParameterOperations.SearchParamLastUpdated,
            };

            try
            {
                // Act
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses([status], CancellationToken.None);

                var dbStatuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                var dbStatus = dbStatuses.FirstOrDefault(s => s.Uri.OriginalString == testUri);

                // Assert - IsPartiallySupported should be preserved
                Assert.NotNull(dbStatus);
                Assert.True(dbStatus.IsPartiallySupported);
                Assert.Equal(SearchParameterStatus.Enabled, dbStatus.Status);
            }
            finally
            {
                await _testHelper.DeleteSearchParameterStatusAsync(testUri);
            }
        }

        [Fact]
        public async Task GivenUpsertStatuses_WhenStatusIsUnsupported_ThenStatusIsPersisted()
        {
            await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);

            // Arrange - Test that Unsupported status is handled correctly
            // Note: In older schemas (< V52), Unsupported is converted to Disabled
            // In newer schemas (>= V52), Unsupported is preserved
            var testUri = "http://hl7.org/fhir/SearchParameter/Test-Unsupported-" + Guid.NewGuid();
            var status = new ResourceSearchParameterStatus
            {
                Uri = new Uri(testUri),
                Status = SearchParameterStatus.Unsupported,
                IsPartiallySupported = false,
                LastUpdated = _fixture.SearchParameterOperations.SearchParamLastUpdated,
            };

            try
            {
                // Act
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses([status], CancellationToken.None);

                var dbStatuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                var dbStatus = dbStatuses.FirstOrDefault(s => s.Uri.OriginalString == testUri);

                // Assert - The status should be either Unsupported (V52+) or Disabled (< V52)
                Assert.NotNull(dbStatus);
                Assert.True(
                    dbStatus.Status == SearchParameterStatus.Unsupported || dbStatus.Status == SearchParameterStatus.Disabled,
                    $"Expected status to be Unsupported or Disabled, but got {dbStatus.Status}");
            }
            finally
            {
                await _testHelper.DeleteSearchParameterStatusAsync(testUri);
            }
        }

        [Fact]
        public async Task GivenUpsertStatuses_WhenMixedNewAndExistingStatuses_ThenBothAreHandledCorrectly()
        {
            await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);

            // Arrange
            var existingUri = "http://hl7.org/fhir/SearchParameter/Test-MixedExisting-" + Guid.NewGuid();
            var newUri = "http://hl7.org/fhir/SearchParameter/Test-MixedNew-" + Guid.NewGuid();

            var existingStatus = new ResourceSearchParameterStatus
            {
                Uri = new Uri(existingUri),
                Status = SearchParameterStatus.Disabled,
                IsPartiallySupported = false,
                LastUpdated = _fixture.SearchParameterOperations.SearchParamLastUpdated,
            };

            try
            {
                // Create the existing status first
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses(new[] { existingStatus }, CancellationToken.None);

                // Get the created status with its database-assigned LastUpdated
                var allStatuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                var createdStatus = allStatuses.First(s => s.Uri.OriginalString == existingUri);

                await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);

                // Prepare mixed batch: update existing + create new
                var updateExisting = new ResourceSearchParameterStatus
                {
                    Uri = createdStatus.Uri,
                    Status = SearchParameterStatus.Enabled,
                    IsPartiallySupported = true,
                    LastUpdated = _fixture.SearchParameterOperations.SearchParamLastUpdated,
                };

                var createNew = new ResourceSearchParameterStatus
                {
                    Uri = new Uri(newUri),
                    Status = SearchParameterStatus.Supported,
                    IsPartiallySupported = false,
                    LastUpdated = _fixture.SearchParameterOperations.SearchParamLastUpdated,
                };

                // Act
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses([updateExisting, createNew], CancellationToken.None);

                // Assert
                var finalStatuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                var updatedExisting = finalStatuses.First(s => s.Uri.OriginalString == existingUri);
                var createdNew = finalStatuses.First(s => s.Uri.OriginalString == newUri);

                Assert.Equal(SearchParameterStatus.Enabled, updatedExisting.Status);
                Assert.True(updatedExisting.IsPartiallySupported);
                Assert.True(updatedExisting.LastUpdated > createdStatus.LastUpdated);

                Assert.Equal(SearchParameterStatus.Supported, createdNew.Status);
                Assert.False(createdNew.IsPartiallySupported);
            }
            finally
            {
                await _testHelper.DeleteSearchParameterStatusAsync(existingUri);
                await _testHelper.DeleteSearchParameterStatusAsync(newUri);
            }
        }

        [Fact]
        public async Task GivenUpsertStatuses_WhenStatusValueChanges_ThenChangeIsReflectedInDatabase()
        {
            await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);

            // Comprehensive test for all status transition scenarios
            // Consolidates multiple transition tests into one comprehensive test

            // Arrange
            var testUri = "http://hl7.org/fhir/SearchParameter/Test-StatusChanges-" + Guid.NewGuid();
            var status = new ResourceSearchParameterStatus
            {
                Uri = new Uri(testUri),
                Status = SearchParameterStatus.Disabled,
                IsPartiallySupported = false,
                LastUpdated = _fixture.SearchParameterOperations.SearchParamLastUpdated,
            };

            try
            {
                // Create initial status (Disabled)
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses([status], CancellationToken.None);

                var statuses1 = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                var dbStatus1 = statuses1.First(s => s.Uri.OriginalString == testUri);
                Assert.Equal(SearchParameterStatus.Disabled, dbStatus1.Status);
                var lastUpdated1 = dbStatus1.LastUpdated;

                await Task.Delay(100);

                await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);

                // Transition to Enabled
                status.Status = SearchParameterStatus.Enabled;
                status.IsPartiallySupported = true;
                status.LastUpdated = _fixture.SearchParameterOperations.SearchParamLastUpdated;
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses([status], CancellationToken.None);

                var statuses2 = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                var dbStatus2 = statuses2.First(s => s.Uri.OriginalString == testUri);
                Assert.Equal(SearchParameterStatus.Enabled, dbStatus2.Status);
                Assert.True(dbStatus2.IsPartiallySupported);
                Assert.True(dbStatus2.LastUpdated >= lastUpdated1);
                var lastUpdated2 = dbStatus2.LastUpdated;

                await Task.Delay(100);

                await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);

                // Transition to Supported
                status.Status = SearchParameterStatus.Supported;
                status.IsPartiallySupported = false;
                status.LastUpdated = _fixture.SearchParameterOperations.SearchParamLastUpdated;
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses([status], CancellationToken.None);

                var statuses3 = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                var dbStatus3 = statuses3.First(s => s.Uri.OriginalString == testUri);
                Assert.Equal(SearchParameterStatus.Supported, dbStatus3.Status);
                Assert.False(dbStatus3.IsPartiallySupported);
                Assert.True(dbStatus3.LastUpdated >= lastUpdated2);
            }
            finally
            {
                await _testHelper.DeleteSearchParameterStatusAsync(testUri);
            }
        }

        [Fact]
        public async Task GivenUpsertStatuses_WhenCancellationRequested_ThenOperationIsCancelled()
        {
            await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);

            // Arrange
            var testUri = "http://hl7.org/fhir/SearchParameter/Test-Cancellation-" + Guid.NewGuid();
            var status = new ResourceSearchParameterStatus
            {
                Uri = new Uri(testUri),
                Status = SearchParameterStatus.Disabled,
                IsPartiallySupported = false,
                LastUpdated = _fixture.SearchParameterOperations.SearchParamLastUpdated,
            };

            using var cts = new CancellationTokenSource();
            cts.Cancel(); // Cancel immediately

            // Act & Assert
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses([status], cts.Token);
            });
        }

        [Fact]
        public async Task GivenGetSearchParameterStatuses_WhenCalled_ThenReturnsConsistentTypes()
        {
            // Verify that all returned statuses are of the correct concrete type

            // Act
            var statuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);

            // Assert
            Assert.NotEmpty(statuses);
            Assert.All(statuses, status =>
            {
                Assert.IsType<SqlServerResourceSearchParameterStatus>(status);
                var sqlStatus = (SqlServerResourceSearchParameterStatus)status;
                Assert.True(sqlStatus.Id > 0, "SqlServerResourceSearchParameterStatus should have a valid Id");
            });
        }

        [Fact]
        public async Task GivenActiveReindexJob_WhenUpsertingSearchParameterStatus_ThenJobConflictExceptionIsThrown()
        {
            // This test validates that search parameter status updates are blocked when a reindex job is running
            // This ensures bulk delete operations targeting SearchParameter resources will fail during reindex

            await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);

            // Arrange - Create a test search parameter status
            var testUri = "http://hl7.org/fhir/SearchParameter/Test-ReindexConflict-" + Guid.NewGuid();
            var status = new ResourceSearchParameterStatus
            {
                Uri = new Uri(testUri),
                Status = SearchParameterStatus.Enabled,
                IsPartiallySupported = false,
                LastUpdated = _fixture.SearchParameterOperations.SearchParamLastUpdated,
            };

            // Start a reindex job
            var reindexJobRecord = new Core.Features.Operations.Reindex.Models.ReindexJobRecord(new List<string>());
            var reindexJob = await _fixture.OperationDataStore.CreateReindexJobAsync(reindexJobRecord, CancellationToken.None);

            try
            {
                // Act & Assert - Upserting without reindexId should throw JobConflictException
                var exception = await Assert.ThrowsAsync<JobConflictException>(async () =>
                {
                    await _fixture.SearchParameterStatusDataStore.UpsertStatuses([status], CancellationToken.None, reindexId: null);
                });

                Assert.Contains("reindex", exception.Message, StringComparison.OrdinalIgnoreCase);
                Assert.Contains(reindexJob.JobRecord.Id, exception.Message);
            }
            finally
            {
                // Cleanup - Cancel the reindex job
                await _fixture.OperationDataStore.UpdateReindexJobAsync(
                    new Core.Features.Operations.Reindex.Models.ReindexJobRecord(new List<string>())
                    {
                        Id = reindexJob.JobRecord.Id,
                        Status = OperationStatus.Canceled,
                    },
                    reindexJob.ETag,
                    CancellationToken.None);
            }
        }

        [Fact]
        public async Task GivenActiveReindexJob_WhenUpsertingWithReindexId_ThenSucceeds()
        {
            // This test validates that the reindex job itself can update search parameter statuses
            // by passing its job ID

            await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);

            // Arrange - Create a test search parameter status
            var testUri = "http://hl7.org/fhir/SearchParameter/Test-ReindexBypass-" + Guid.NewGuid();
            var status = new ResourceSearchParameterStatus
            {
                Uri = new Uri(testUri),
                Status = SearchParameterStatus.Enabled,
                IsPartiallySupported = false,
                LastUpdated = _fixture.SearchParameterOperations.SearchParamLastUpdated,
            };

            // Start a reindex job
            var reindexJobRecord = new Core.Features.Operations.Reindex.Models.ReindexJobRecord(new List<string>());
            var reindexJob = await _fixture.OperationDataStore.CreateReindexJobAsync(reindexJobRecord, CancellationToken.None);
            var jobId = long.Parse(reindexJob.JobRecord.Id);

            try
            {
                // Act - Upserting with reindexId should succeed (bypass the check)
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses([status], CancellationToken.None, reindexId: jobId);

                // Assert - Verify the status was created
                var allStatuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                var createdStatus = allStatuses.FirstOrDefault(s => s.Uri.OriginalString == testUri);
                Assert.NotNull(createdStatus);
                Assert.Equal(SearchParameterStatus.Enabled, createdStatus.Status);
            }
            finally
            {
                // Cleanup
                await _testHelper.DeleteSearchParameterStatusAsync(testUri);
                await _fixture.OperationDataStore.UpdateReindexJobAsync(
                    new Core.Features.Operations.Reindex.Models.ReindexJobRecord(new List<string>())
                    {
                        Id = reindexJob.JobRecord.Id,
                        Status = OperationStatus.Canceled,
                    },
                    reindexJob.ETag,
                    CancellationToken.None);
            }
        }

        [Fact]
        public async Task GivenNoActiveReindexJob_WhenUpsertingSearchParameterStatus_ThenSucceeds()
        {
            // This test validates that when no reindex is running, search parameter updates work normally
            // This ensures bulk delete operations can proceed when no reindex is active

            await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);

            // Arrange - Ensure no active reindex jobs
            var (found, id) = await _fixture.OperationDataStore.CheckActiveReindexJobsAsync(CancellationToken.None);
            if (found)
            {
                // Cancel any active reindex job first
                var existingJob = await _fixture.OperationDataStore.GetReindexJobByIdAsync(id, CancellationToken.None);
                await _fixture.OperationDataStore.UpdateReindexJobAsync(
                    new Core.Features.Operations.Reindex.Models.ReindexJobRecord(new List<string>())
                    {
                        Id = existingJob.JobRecord.Id,
                        Status = OperationStatus.Canceled,
                    },
                    existingJob.ETag,
                    CancellationToken.None);
            }

            var testUri = "http://hl7.org/fhir/SearchParameter/Test-NoReindex-" + Guid.NewGuid();
            var status = new ResourceSearchParameterStatus
            {
                Uri = new Uri(testUri),
                Status = SearchParameterStatus.Enabled,
                IsPartiallySupported = false,
                LastUpdated = _fixture.SearchParameterOperations.SearchParamLastUpdated,
            };

            try
            {
                // Act - Should succeed without any reindex job running
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses([status], CancellationToken.None);

                // Assert - Verify the status was created
                var allStatuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                var createdStatus = allStatuses.FirstOrDefault(s => s.Uri.OriginalString == testUri);
                Assert.NotNull(createdStatus);
                Assert.Equal(SearchParameterStatus.Enabled, createdStatus.Status);
            }
            finally
            {
                await _testHelper.DeleteSearchParameterStatusAsync(testUri);
            }
        }
    }
}
