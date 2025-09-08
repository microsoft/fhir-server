// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Storage.Registry;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
    public class SearchParameterOptimisticConcurrencyIntegrationTests : IClassFixture<FhirStorageTestsFixture>
    {
        private readonly FhirStorageTestsFixture _fixture;
        private readonly IFhirStorageTestHelper _testHelper;

        public SearchParameterOptimisticConcurrencyIntegrationTests(FhirStorageTestsFixture fixture)
        {
            _fixture = fixture;
            _testHelper = fixture.TestHelper;
        }

        [Fact]
        public async Task GivenSchemaVersion94OrHigher_WhenGettingSearchParameterStatuses_ThenLastUpdatedIsReturned()
        {
            // Act
            var statuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);

            // Assert
            Assert.NotEmpty(statuses);

            // Verify that all statuses have LastUpdated (all parameters should have LastUpdated values)
            foreach (var status in statuses)
            {
                Assert.True(status.LastUpdated != default(DateTimeOffset), "All search parameter statuses should have valid LastUpdated values");
            }
        }

        [Fact]
        public async Task GivenNewSearchParameterStatus_WhenUpserting_ThenLastUpdatedIsReturned()
        {
            // Arrange
            var testUri = $"http://test.com/SearchParameter/ConcurrencyTest_{Guid.NewGuid()}";
            var newStatus = new ResourceSearchParameterStatus
            {
                Uri = new Uri(testUri),
                Status = SearchParameterStatus.Disabled,
                IsPartiallySupported = false,
                LastUpdated = default(DateTimeOffset), // New parameter, no previous LastUpdated
            };

            try
            {
                // Act
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses(new[] { newStatus }, CancellationToken.None);

                // Get the upserted status to check LastUpdated was assigned
                var allStatuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                var upsertedStatus = allStatuses.FirstOrDefault(s => s.Uri.ToString() == testUri);

                // Assert
                Assert.NotNull(upsertedStatus);
                Assert.True(upsertedStatus.LastUpdated != default(DateTimeOffset), "LastUpdated should be assigned for new parameters");
            }
            finally
            {
                // Cleanup
                await _testHelper.DeleteSearchParameterStatusAsync(testUri);
            }
        }

        [Fact]
        public async Task GivenExistingSearchParameterStatus_WhenUpdatingWithCorrectLastUpdated_ThenSucceeds()
        {
            // Arrange
            var testUri = $"http://test.com/SearchParameter/ConcurrencyTest_{Guid.NewGuid()}";
            var initialStatus = new ResourceSearchParameterStatus
            {
                Uri = new Uri(testUri),
                Status = SearchParameterStatus.Disabled,
                IsPartiallySupported = false,
            };

            try
            {
                // Create initial status
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses(new[] { initialStatus }, CancellationToken.None);

                // Get the created status with its LastUpdated
                var allStatuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                var createdStatus = allStatuses.First(s => s.Uri.ToString() == testUri);

                // Modify and update with correct LastUpdated
                var updatedStatus = new ResourceSearchParameterStatus
                {
                    Uri = createdStatus.Uri,
                    Status = SearchParameterStatus.Enabled, // Changed status
                    IsPartiallySupported = true, // Changed partially supported
                    LastUpdated = createdStatus.LastUpdated, // Use the current LastUpdated
                };

                // Act
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses(new[] { updatedStatus }, CancellationToken.None);

                // Verify the update succeeded
                var updatedAllStatuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                var result = updatedAllStatuses.First(s => s.Uri.ToString() == testUri);

                // Assert
                Assert.Equal(SearchParameterStatus.Enabled, result.Status);
                Assert.True(result.IsPartiallySupported);
                Assert.True(result.LastUpdated > createdStatus.LastUpdated, "LastUpdated should change after update");
            }
            finally
            {
                // Cleanup
                await _testHelper.DeleteSearchParameterStatusAsync(testUri);
            }
        }

        [Fact]
        public async Task GivenExistingSearchParameterStatus_WhenUpdatingWithIncorrectLastUpdated_ThenEventuallySucceedsWithRetry()
        {
            // Arrange
            var testUri = $"http://test.com/SearchParameter/ConcurrencyTest_{Guid.NewGuid()}";
            var initialStatus = new ResourceSearchParameterStatus
            {
                Uri = new Uri(testUri),
                Status = SearchParameterStatus.Disabled,
                IsPartiallySupported = false,
            };

            try
            {
                // Create initial status
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses(new[] { initialStatus }, CancellationToken.None);

                // Get the created status with its LastUpdated
                var allStatuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                var createdStatus = allStatuses.First(s => s.Uri.ToString() == testUri);

                // Make an intermediate update to change the LastUpdated
                var intermediateUpdate = new ResourceSearchParameterStatus
                {
                    Uri = createdStatus.Uri,
                    Status = SearchParameterStatus.Enabled,
                    IsPartiallySupported = false,
                    LastUpdated = createdStatus.LastUpdated,
                };
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses(new[] { intermediateUpdate }, CancellationToken.None);

                // Now try to update with the stale LastUpdated
                // The retry mechanism should detect the conflict, refresh the LastUpdated, and succeed
                var staleUpdate = new ResourceSearchParameterStatus
                {
                    Uri = createdStatus.Uri,
                    Status = SearchParameterStatus.Supported,
                    IsPartiallySupported = true,
                    LastUpdated = createdStatus.LastUpdated, // This is now stale
                };

                // Act - This should succeed due to retry mechanism
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses(new[] { staleUpdate }, CancellationToken.None);

                // Assert - Verify the final status shows the update succeeded
                var finalStatuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                var finalStatus = finalStatuses.First(s => s.Uri.ToString() == testUri);

                // The retry mechanism should have allowed the update to succeed
                Assert.Equal(SearchParameterStatus.Supported, finalStatus.Status);
                Assert.True(finalStatus.IsPartiallySupported);
            }
            finally
            {
                // Cleanup
                await _testHelper.DeleteSearchParameterStatusAsync(testUri);
            }
        }

        [Fact]
        public async Task GivenMultipleConsecutiveStaleUpdates_WhenUpdatingSearchParameter_ThenRetryMechanismIsTriggeredMultipleTimes()
        {
            // Arrange
            var testUri = $"http://test.com/SearchParameter/RetryTest_{Guid.NewGuid()}";
            var initialStatus = new ResourceSearchParameterStatus
            {
                Uri = new Uri(testUri),
                Status = SearchParameterStatus.Disabled,
                IsPartiallySupported = false,
            };

            try
            {
                // Create initial status
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses(new[] { initialStatus }, CancellationToken.None);

                // Get the created status with its LastUpdated
                var allStatuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                var createdStatus = allStatuses.First(s => s.Uri.ToString() == testUri);
                var originalLastUpdated = createdStatus.LastUpdated;

                // Force multiple intermediate updates to create staleness
                for (int i = 0; i < 3; i++)
                {
                    var intermediateUpdate = new ResourceSearchParameterStatus
                    {
                        Uri = createdStatus.Uri,
                        Status = SearchParameterStatus.Enabled,
                        IsPartiallySupported = false,
                        LastUpdated = createdStatus.LastUpdated,
                    };
                    await _fixture.SearchParameterStatusDataStore.UpsertStatuses(new[] { intermediateUpdate }, CancellationToken.None);

                    // Refresh status for next iteration
                    allStatuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                    createdStatus = allStatuses.First(s => s.Uri.ToString() == testUri);
                }

                // Now attempt update with very stale LastUpdated - this should trigger retry
                var veryStaleUpdate = new ResourceSearchParameterStatus
                {
                    Uri = createdStatus.Uri,
                    Status = SearchParameterStatus.Supported,
                    IsPartiallySupported = true,
                    LastUpdated = originalLastUpdated, // This is very stale (3 updates behind)
                };

                // Act - This should succeed due to retry mechanism
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses(new[] { veryStaleUpdate }, CancellationToken.None);

                // Assert - Verify the final status shows the update succeeded despite staleness
                var finalStatuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                var finalStatus = finalStatuses.First(s => s.Uri.ToString() == testUri);

                Assert.Equal(SearchParameterStatus.Supported, finalStatus.Status);
                Assert.True(finalStatus.IsPartiallySupported);
                Assert.True(finalStatus.LastUpdated > originalLastUpdated, "LastUpdated should have changed significantly");
            }
            finally
            {
                // Cleanup
                await _testHelper.DeleteSearchParameterStatusAsync(testUri);
            }
        }

        [Fact]
        public async Task GivenRapidConcurrentUpdates_WhenUsingStaleLastUpdated_ThenRetryMechanismHandlesHighContentionScenario()
        {
            // Arrange
            var testUri = $"http://test.com/SearchParameter/HighContentionTest_{Guid.NewGuid()}";
            var initialStatus = new ResourceSearchParameterStatus
            {
                Uri = new Uri(testUri),
                Status = SearchParameterStatus.Disabled,
                IsPartiallySupported = false,
            };

            try
            {
                // Create initial status
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses(new[] { initialStatus }, CancellationToken.None);

                // Get the created status with its LastUpdated
                var allStatuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                var createdStatus = allStatuses.First(s => s.Uri.ToString() == testUri);
                var originalLastUpdated = createdStatus.LastUpdated;

                // Create high contention by launching multiple rapid updates
                var rapidUpdateTasks = new List<Task>();
                for (int i = 0; i < 5; i++)
                {
                    var updateIndex = i;
                    rapidUpdateTasks.Add(Task.Run(async () =>
                    {
                        var rapidUpdate = new ResourceSearchParameterStatus
                        {
                            Uri = createdStatus.Uri,
                            Status = SearchParameterStatus.Enabled,
                            IsPartiallySupported = updateIndex % 2 == 0,
                            LastUpdated = createdStatus.LastUpdated, // Using same (potentially stale) LastUpdated
                        };
                        await _fixture.SearchParameterStatusDataStore.UpsertStatuses(new[] { rapidUpdate }, CancellationToken.None);
                    }));
                }

                // Act - Wait for all rapid updates to complete (some may trigger retries due to contention)
                await Task.WhenAll(rapidUpdateTasks);

                // Now attempt one final update with the original (definitely stale) LastUpdated
                var finalStaleUpdate = new ResourceSearchParameterStatus
                {
                    Uri = createdStatus.Uri,
                    Status = SearchParameterStatus.Supported,
                    IsPartiallySupported = true,
                    LastUpdated = originalLastUpdated, // This is definitely stale after all the rapid updates
                };

                await _fixture.SearchParameterStatusDataStore.UpsertStatuses(new[] { finalStaleUpdate }, CancellationToken.None);

                // Assert - Verify the final update succeeded despite high contention
                var finalStatuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                var finalStatus = finalStatuses.First(s => s.Uri.ToString() == testUri);

                Assert.Equal(SearchParameterStatus.Supported, finalStatus.Status);
                Assert.True(finalStatus.IsPartiallySupported);
                Assert.True(finalStatus.LastUpdated > originalLastUpdated, "LastUpdated should have changed after high contention scenario");
            }
            finally
            {
                // Cleanup
                await _testHelper.DeleteSearchParameterStatusAsync(testUri);
            }
        }

        [Fact]
        public async Task GivenOptimisticConcurrencyDetection_WhenLastUpdatedChangesBeforeUpdate_ThenRetryMechanismHandlesConflict()
        {
            // Arrange
            var testUri = $"http://test.com/SearchParameter/ConcurrencyTest_{Guid.NewGuid()}";
            var initialStatus = new ResourceSearchParameterStatus
            {
                Uri = new Uri(testUri),
                Status = SearchParameterStatus.Disabled,
                IsPartiallySupported = false,
            };

            try
            {
                // Create initial status
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses(new[] { initialStatus }, CancellationToken.None);

                // Get the created status with its LastUpdated
                var allStatuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                var createdStatus = allStatuses.First(s => s.Uri.ToString() == testUri);
                var originalLastUpdated = createdStatus.LastUpdated;

                // First, update with current LastUpdated to change it
                var intermediateUpdate = new ResourceSearchParameterStatus
                {
                    Uri = createdStatus.Uri,
                    Status = SearchParameterStatus.Enabled,
                    IsPartiallySupported = false,
                    LastUpdated = originalLastUpdated,
                };

                await _fixture.SearchParameterStatusDataStore.UpsertStatuses(new[] { intermediateUpdate }, CancellationToken.None);

                // Now try to update with the stale LastUpdated - this should trigger retry mechanism
                var staleUpdate = new ResourceSearchParameterStatus
                {
                    Uri = createdStatus.Uri,
                    Status = SearchParameterStatus.Supported,
                    IsPartiallySupported = true,
                    LastUpdated = originalLastUpdated, // This is now stale
                };

                // Act - This should succeed due to retry mechanism
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses(new[] { staleUpdate }, CancellationToken.None);

                // Verify the final status
                var finalStatuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                var finalStatus = finalStatuses.First(s => s.Uri.ToString() == testUri);

                // Assert - The update should have succeeded (with retry)
                Assert.Equal(SearchParameterStatus.Supported, finalStatus.Status);
                Assert.True(finalStatus.IsPartiallySupported);
                Assert.True(finalStatus.LastUpdated > originalLastUpdated, "LastUpdated should have changed");
            }
            finally
            {
                // Cleanup
                await _testHelper.DeleteSearchParameterStatusAsync(testUri);
            }
        }

        [Fact]
        public async Task GivenMixedUpdatesWithAndWithoutRowVersion_WhenUpserting_ThenBothSucceed()
        {
            // Arrange
            var existingTestUri = $"http://test.com/SearchParameter/Existing_{Guid.NewGuid()}";
            var newTestUri = $"http://test.com/SearchParameter/New_{Guid.NewGuid()}_";

            var existingStatus = new ResourceSearchParameterStatus
            {
                Uri = new Uri(existingTestUri),
                Status = SearchParameterStatus.Disabled,
                IsPartiallySupported = false,
            };

            try
            {
                // Create existing parameter
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses(new[] { existingStatus }, CancellationToken.None);

                // Get the created status with its LastUpdated
                var allStatuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                var createdStatus = allStatuses.First(s => s.Uri.ToString() == existingTestUri);

                // Prepare mixed updates
                var updateExisting = new ResourceSearchParameterStatus
                {
                    Uri = createdStatus.Uri,
                    Status = SearchParameterStatus.Enabled,
                    IsPartiallySupported = true,
                    LastUpdated = createdStatus.LastUpdated, // With LastUpdated
                };

                var createNew = new ResourceSearchParameterStatus
                {
                    Uri = new Uri(newTestUri),
                    Status = SearchParameterStatus.Supported,
                    IsPartiallySupported = false,
                    LastUpdated = default(DateTimeOffset), // Without LastUpdated (new parameter)
                };

                // Act
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses(new[] { updateExisting, createNew }, CancellationToken.None);

                // Verify both operations succeeded
                var updatedAllStatuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                var updatedExisting = updatedAllStatuses.First(r => r.Uri.ToString() == existingTestUri);
                var createdNew = updatedAllStatuses.First(r => r.Uri.ToString() == newTestUri);

                // Assert
                Assert.Equal(SearchParameterStatus.Enabled, updatedExisting.Status);
                Assert.True(updatedExisting.IsPartiallySupported);
                Assert.True(updatedExisting.LastUpdated > createdStatus.LastUpdated, "Updated parameter should have newer LastUpdated");

                Assert.Equal(SearchParameterStatus.Supported, createdNew.Status);
                Assert.False(createdNew.IsPartiallySupported);
                Assert.True(createdNew.LastUpdated != default(DateTimeOffset), "New parameter should have valid LastUpdated");
            }
            finally
            {
                // Cleanup
                await _testHelper.DeleteSearchParameterStatusAsync(existingTestUri);
                await _testHelper.DeleteSearchParameterStatusAsync(newTestUri);
            }
        }
    }
}
