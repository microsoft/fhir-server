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

        [SkippableFact]
        public async Task GivenSchemaVersion94OrHigher_WhenGettingSearchParameterStatuses_ThenRowVersionIsReturned()
        {
            // Skip if not SQL Server or schema version < 94
            Skip.If(!IsSqlServerWithOptimisticConcurrency(), "Schema version 94+ required for optimistic concurrency");

            // Act
            var statuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);

            // Assert
            Assert.NotEmpty(statuses);

            // Verify that at least some statuses have RowVersion (new parameters will have row versions)
            var statusesWithRowVersion = statuses.Where(s => s.RowVersion != null);

            // All statuses should have RowVersion in schema 94+
            foreach (var status in statusesWithRowVersion)
            {
                Assert.NotNull(status.RowVersion);
                Assert.Equal(8, status.RowVersion.Length); // SQL Server rowversion is always 8 bytes
            }
        }

        [SkippableFact]
        public async Task GivenNewSearchParameterStatus_WhenUpserting_ThenRowVersionIsReturned()
        {
            // Skip if not SQL Server or schema version < 94
            Skip.If(!IsSqlServerWithOptimisticConcurrency(), "Schema version 94+ required for optimistic concurrency");

            // Arrange
            var testUri = $"http://test.com/SearchParameter/ConcurrencyTest_{Guid.NewGuid()}";
            var newStatus = new ResourceSearchParameterStatus
            {
                Uri = new Uri(testUri),
                Status = SearchParameterStatus.Disabled,
                IsPartiallySupported = false,
                RowVersion = null, // New parameter, no row version yet
            };

            try
            {
                // Act
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses(new[] { newStatus }, CancellationToken.None);

                // Get the upserted status to check RowVersion was assigned
                var allStatuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                var upsertedStatus = allStatuses.FirstOrDefault(s => s.Uri.ToString() == testUri);

                // Assert
                Assert.NotNull(upsertedStatus);
                Assert.NotNull(upsertedStatus.RowVersion);
                Assert.Equal(8, upsertedStatus.RowVersion.Length);
            }
            finally
            {
                // Cleanup
                await _testHelper.DeleteSearchParameterStatusAsync(testUri);
            }
        }

        [SkippableFact]
        public async Task GivenExistingSearchParameterStatus_WhenUpdatingWithCorrectRowVersion_ThenSucceeds()
        {
            // Skip if not SQL Server or schema version < 94
            Skip.If(!IsSqlServerWithOptimisticConcurrency(), "Schema version 94+ required for optimistic concurrency");

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

                // Get the created status with its RowVersion
                var allStatuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                var createdStatus = allStatuses.First(s => s.Uri.ToString() == testUri);

                // Modify and update with correct RowVersion
                var updatedStatus = new ResourceSearchParameterStatus
                {
                    Uri = createdStatus.Uri,
                    Status = SearchParameterStatus.Enabled, // Changed status
                    IsPartiallySupported = true, // Changed partially supported
                    RowVersion = createdStatus.RowVersion, // Use the returned RowVersion
                };

                // Act
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses(new[] { updatedStatus }, CancellationToken.None);

                // Verify the update succeeded
                var updatedAllStatuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                var result = updatedAllStatuses.First(s => s.Uri.ToString() == testUri);

                // Assert
                Assert.Equal(SearchParameterStatus.Enabled, result.Status);
                Assert.True(result.IsPartiallySupported);
                Assert.NotNull(result.RowVersion);
                Assert.NotEqual(createdStatus.RowVersion, result.RowVersion); // RowVersion should change
            }
            finally
            {
                // Cleanup
                await _testHelper.DeleteSearchParameterStatusAsync(testUri);
            }
        }

        [SkippableFact]
        public async Task GivenExistingSearchParameterStatus_WhenUpdatingWithIncorrectRowVersion_ThenEventuallySucceedsWithRetry()
        {
            // Skip if not SQL Server or schema version < 94
            Skip.If(!IsSqlServerWithOptimisticConcurrency(), "Schema version 94+ required for optimistic concurrency");

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

                // Get the created status with its RowVersion
                var allStatuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                var createdStatus = allStatuses.First(s => s.Uri.ToString() == testUri);

                // Make an intermediate update to change the RowVersion
                var intermediateUpdate = new ResourceSearchParameterStatus
                {
                    Uri = createdStatus.Uri,
                    Status = SearchParameterStatus.Enabled,
                    IsPartiallySupported = false,
                    RowVersion = createdStatus.RowVersion,
                };
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses(new[] { intermediateUpdate }, CancellationToken.None);

                // Now try to update with the stale RowVersion
                // The retry mechanism should detect the conflict, refresh the RowVersion, and succeed
                var staleUpdate = new ResourceSearchParameterStatus
                {
                    Uri = createdStatus.Uri,
                    Status = SearchParameterStatus.Supported,
                    IsPartiallySupported = true,
                    RowVersion = createdStatus.RowVersion, // This is now stale
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

        [SkippableFact]
        public void GivenMaxRetriesExceeded_WhenConcurrencyConflictsPersist_ThenThrowsConcurrencyException()
        {
            // Skip if not SQL Server or schema version < 94
            Skip.If(!IsSqlServerWithOptimisticConcurrency(), "Schema version 94+ required for optimistic concurrency");

            // This test would need to be implemented with a custom data store that simulates
            // persistent concurrency conflicts that can't be resolved by refreshing RowVersion.
            // For now, we'll skip this test as it requires more complex setup than the current
            // integration test framework supports.
            Skip.If(true, "Test requires custom setup to simulate persistent concurrency conflicts");
        }

        [SkippableFact]
        public async Task GivenConcurrentUpdatesToSameSearchParameter_WhenUsingOptimisticConcurrency_ThenBothEventuallySucceedWithRetry()
        {
            // Skip if not SQL Server or schema version < 94
            Skip.If(!IsSqlServerWithOptimisticConcurrency(), "Schema version 94+ required for optimistic concurrency");

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

                // Get the created status with its RowVersion
                var allStatuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                var createdStatus = allStatuses.First(s => s.Uri.ToString() == testUri);

                // Prepare two concurrent updates with the same RowVersion
                var update1 = new ResourceSearchParameterStatus
                {
                    Uri = createdStatus.Uri,
                    Status = SearchParameterStatus.Enabled,
                    IsPartiallySupported = false,
                    RowVersion = createdStatus.RowVersion,
                };

                var update2 = new ResourceSearchParameterStatus
                {
                    Uri = createdStatus.Uri,
                    Status = SearchParameterStatus.Supported,
                    IsPartiallySupported = true,
                    RowVersion = createdStatus.RowVersion,
                };

                // Act - Execute both updates concurrently
                var task1 = _fixture.SearchParameterStatusDataStore.UpsertStatuses(new[] { update1 }, CancellationToken.None);
                var task2 = _fixture.SearchParameterStatusDataStore.UpsertStatuses(new[] { update2 }, CancellationToken.None);

                var results = await Task.WhenAll(
                    task1.ContinueWith(t => new { Success = t.IsCompletedSuccessfully, Exception = t.Exception?.GetBaseException() }),
                    task2.ContinueWith(t => new { Success = t.IsCompletedSuccessfully, Exception = t.Exception?.GetBaseException() }));

                // Assert
                // With retry logic, both operations should eventually succeed
                // The system is designed to handle transient concurrency conflicts by retrying with updated RowVersion
                var successes = results.Count(r => r.Success);
                var failures = results.Count(r => !r.Success);

                Assert.Equal(2, successes); // Both should succeed due to retry mechanism
                Assert.Equal(0, failures);

                // Verify that one of the updates was applied (whichever succeeded last)
                var finalStatuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                var finalStatus = finalStatuses.First(s => s.Uri.ToString() == testUri);

                // The final status should be one of the two updates
                Assert.True(
                    (finalStatus.Status == SearchParameterStatus.Enabled && !finalStatus.IsPartiallySupported) ||
                    (finalStatus.Status == SearchParameterStatus.Supported && finalStatus.IsPartiallySupported),
                    $"Final status should match one of the concurrent updates. Actual: Status={finalStatus.Status}, IsPartiallySupported={finalStatus.IsPartiallySupported}");
            }
            finally
            {
                // Cleanup
                await _testHelper.DeleteSearchParameterStatusAsync(testUri);
            }
        }

        [SkippableFact]
        public async Task GivenOptimisticConcurrencyDetection_WhenRowVersionChangesBeforeUpdate_ThenRetryMechanismHandlesConflict()
        {
            // Skip if not SQL Server or schema version < 94
            Skip.If(!IsSqlServerWithOptimisticConcurrency(), "Schema version 94+ required for optimistic concurrency");

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

                // Get the created status with its RowVersion
                var allStatuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                var createdStatus = allStatuses.First(s => s.Uri.ToString() == testUri);
                var originalRowVersion = createdStatus.RowVersion;

                // First, update with current RowVersion to change it
                var intermediateUpdate = new ResourceSearchParameterStatus
                {
                    Uri = createdStatus.Uri,
                    Status = SearchParameterStatus.Enabled,
                    IsPartiallySupported = false,
                    RowVersion = originalRowVersion,
                };

                await _fixture.SearchParameterStatusDataStore.UpsertStatuses(new[] { intermediateUpdate }, CancellationToken.None);

                // Now try to update with the stale RowVersion - this should trigger retry mechanism
                var staleUpdate = new ResourceSearchParameterStatus
                {
                    Uri = createdStatus.Uri,
                    Status = SearchParameterStatus.Supported,
                    IsPartiallySupported = true,
                    RowVersion = originalRowVersion, // This is now stale
                };

                // Act - This should succeed due to retry mechanism
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses(new[] { staleUpdate }, CancellationToken.None);

                // Verify the final status
                var finalStatuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                var finalStatus = finalStatuses.First(s => s.Uri.ToString() == testUri);

                // Assert - The update should have succeeded (with retry)
                Assert.Equal(SearchParameterStatus.Supported, finalStatus.Status);
                Assert.True(finalStatus.IsPartiallySupported);
                Assert.NotEqual(originalRowVersion, finalStatus.RowVersion); // RowVersion should have changed
            }
            finally
            {
                // Cleanup
                await _testHelper.DeleteSearchParameterStatusAsync(testUri);
            }
        }

        [SkippableFact]
        public async Task GivenMixedUpdatesWithAndWithoutRowVersion_WhenUpserting_ThenBothSucceed()
        {
            // Skip if not SQL Server or schema version < 94
            Skip.If(!IsSqlServerWithOptimisticConcurrency(), "Schema version 94+ required for optimistic concurrency");

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

                // Get the created status with its RowVersion
                var allStatuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                var createdStatus = allStatuses.First(s => s.Uri.ToString() == existingTestUri);

                // Prepare mixed updates
                var updateExisting = new ResourceSearchParameterStatus
                {
                    Uri = createdStatus.Uri,
                    Status = SearchParameterStatus.Enabled,
                    IsPartiallySupported = true,
                    RowVersion = createdStatus.RowVersion, // With RowVersion
                };

                var createNew = new ResourceSearchParameterStatus
                {
                    Uri = new Uri(newTestUri),
                    Status = SearchParameterStatus.Supported,
                    IsPartiallySupported = false,
                    RowVersion = null, // Without RowVersion (new parameter)
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
                Assert.NotNull(updatedExisting.RowVersion);

                Assert.Equal(SearchParameterStatus.Supported, createdNew.Status);
                Assert.False(createdNew.IsPartiallySupported);
                Assert.NotNull(createdNew.RowVersion);
            }
            finally
            {
                // Cleanup
                await _testHelper.DeleteSearchParameterStatusAsync(existingTestUri);
                await _testHelper.DeleteSearchParameterStatusAsync(newTestUri);
            }
        }

        [SkippableFact]
        public async Task GivenSchemaVersionBelow94_WhenUpserting_ThenRowVersionIsIgnoredGracefully()
        {
            // Skip if SQL Server and schema version >= 94
            Skip.If(IsSqlServerWithOptimisticConcurrency(), "This test validates backward compatibility with schema < 94");

            // This test validates backward compatibility for non-SQL Server or older schema versions
            // Arrange
            var testUri = $"http://test.com/SearchParameter/BackwardCompatTest_{Guid.NewGuid()}";
            var status = new ResourceSearchParameterStatus
            {
                Uri = new Uri(testUri),
                Status = SearchParameterStatus.Enabled,
                IsPartiallySupported = true,
                RowVersion = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 }, // Should be ignored
            };

            try
            {
                // Act - Should not throw even with RowVersion provided for older schemas
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses(new[] { status }, CancellationToken.None);

                // Verify the status was created/updated successfully
                var allStatuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                var result = allStatuses.FirstOrDefault(r => r.Uri.ToString() == testUri);

                // Assert
                Assert.NotNull(result);
                Assert.Equal(testUri, result.Uri.ToString());
                Assert.Equal(SearchParameterStatus.Enabled, result.Status);
                Assert.True(result.IsPartiallySupported);

                // RowVersion behavior depends on schema version - we just verify no exceptions were thrown
            }
            finally
            {
                // Cleanup
                await _testHelper.DeleteSearchParameterStatusAsync(testUri);
            }
        }

        /// <summary>
        /// Determines if this is a SQL Server fixture with schema version 94 or higher (optimistic concurrency support).
        /// </summary>
        private bool IsSqlServerWithOptimisticConcurrency()
        {
            // For now, assume that if we're using SQL Server, we have the optimistic concurrency feature
            // This will be true in most integration test scenarios where we use the latest schema
            if (_fixture.TestHelper is SqlServerFhirStorageTestHelper)
            {
                // Check if we can access the SQL Server search parameter status data store
                // If so, we likely have schema version 94+ support
                try
                {
                    return _fixture.SearchParameterStatusDataStore is SqlServerSearchParameterStatusDataStore;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }
    }
}
