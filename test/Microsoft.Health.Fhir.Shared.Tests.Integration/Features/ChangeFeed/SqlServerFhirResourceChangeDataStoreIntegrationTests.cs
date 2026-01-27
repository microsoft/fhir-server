// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Features.ChangeFeed;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Features.ChangeFeed
{
    /// <summary>
    /// Additional integration tests for SqlServerFhirResourceChangeDataStore
    /// focusing on edge cases, error handling, and parameter validation.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DomainLogicValidation)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class SqlServerFhirResourceChangeDataStoreIntegrationTests : IClassFixture<SqlServerFhirResourceChangeCaptureFixture>
    {
        private readonly SqlServerFhirResourceChangeCaptureFixture _fixture;

        public SqlServerFhirResourceChangeDataStoreIntegrationTests(SqlServerFhirResourceChangeCaptureFixture fixture)
        {
            _fixture = fixture;
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-100)]
        public async Task GetRecordsAsync_WithInvalidStartId_ThrowsArgumentOutOfRangeException(long invalidStartId)
        {
            // Arrange
            var resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(
                _fixture.SqlConnectionWrapperFactory,
                NullLogger<SqlServerFhirResourceChangeDataStore>.Instance,
                _fixture.SchemaInformation);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await resourceChangeDataStore.GetRecordsAsync(invalidStartId, 10, CancellationToken.None));

            Assert.Equal("startId", exception.ParamName);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-100)]
        public async Task GetRecordsAsync_WithInvalidPageSize_ThrowsArgumentOutOfRangeException(short invalidPageSize)
        {
            // Arrange
            var resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(
                _fixture.SqlConnectionWrapperFactory,
                NullLogger<SqlServerFhirResourceChangeDataStore>.Instance,
                _fixture.SchemaInformation);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await resourceChangeDataStore.GetRecordsAsync(1, invalidPageSize, CancellationToken.None));

            Assert.Equal("pageSize", exception.ParamName);
        }

        [Fact]
        public async Task GetRecordsAsync_WithCancellationToken_CanBeCancelled()
        {
            // Arrange
            var resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(
                _fixture.SqlConnectionWrapperFactory,
                NullLogger<SqlServerFhirResourceChangeDataStore>.Instance,
                _fixture.SchemaInformation);

            using var cts = new CancellationTokenSource();
            cts.Cancel(); // Cancel immediately

            // Act & Assert
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await resourceChangeDataStore.GetRecordsAsync(1, 10, cts.Token));
        }

        [Fact]
        public async Task GetRecordsAsync_WithEmptyResult_ReturnsEmptyCollection()
        {
            // Arrange
            var resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(
                _fixture.SqlConnectionWrapperFactory,
                NullLogger<SqlServerFhirResourceChangeDataStore>.Instance,
                _fixture.SchemaInformation);

            // Act - Query with a very high start ID that doesn't exist
            var result = await resourceChangeDataStore.GetRecordsAsync(long.MaxValue - 1, 10, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetRecordsAsync_WithSmallPageSize_ReturnsLimitedResults()
        {
            // Arrange
            // First, create some test data
            await _fixture.Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));
            await _fixture.Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));
            await _fixture.Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));

            var resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(
                _fixture.SqlConnectionWrapperFactory,
                NullLogger<SqlServerFhirResourceChangeDataStore>.Instance,
                _fixture.SchemaInformation);

            // Act - Request only 1 record
            var result = await resourceChangeDataStore.GetRecordsAsync(1, 1, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Count <= 1, $"Expected at most 1 result, got {result.Count}");
        }

        [Fact]
        public async Task GetRecordsAsync_WithLargePageSize_ReturnsAllAvailableResults()
        {
            // Arrange
            // Create test data to ensure there's something to retrieve
            for (int i = 0; i < 3; i++)
            {
                await _fixture.Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));
            }

            var resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(
                _fixture.SqlConnectionWrapperFactory,
                NullLogger<SqlServerFhirResourceChangeDataStore>.Instance,
                _fixture.SchemaInformation);

            // Act - Request with very large page size
            var result = await resourceChangeDataStore.GetRecordsAsync(1, short.MaxValue, CancellationToken.None);

            // Assert
            Assert.NotNull(result);

            // Should return all available records without throwing an exception
            // Verify that the method handles large page sizes gracefully
            Assert.True(result.Count >= 3, $"Expected at least 3 records (the ones we just created), got {result.Count}");

            // Verify all records are valid
            Assert.All(result, change =>
            {
                Assert.True(change.Id > 0);
                Assert.NotNull(change.ResourceId);
                Assert.NotEmpty(change.ResourceTypeName);
            });
        }

        [Fact]
        public async Task GetRecordsAsync_WithDateTime_ReturnsRecordsAfterCheckpoint()
        {
            // Arrange
            var checkpoint = DateTime.UtcNow.AddMinutes(-10);

            // Create a resource after the checkpoint
            var saveResult = await _fixture.Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));
            var resourceId = saveResult.RawResourceElement.Id;

            var resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(
                _fixture.SqlConnectionWrapperFactory,
                NullLogger<SqlServerFhirResourceChangeDataStore>.Instance,
                _fixture.SchemaInformation);

            // Act
            var result = await resourceChangeDataStore.GetRecordsAsync(1, checkpoint, 100, CancellationToken.None);

            // Assert
            Assert.NotNull(result);

            // Verify records created after the checkpoint are returned
            var resourceChange = result.FirstOrDefault(x => x.ResourceId == resourceId);
            Assert.NotNull(resourceChange);
            Assert.True(
                resourceChange.Timestamp >= checkpoint,
                $"Resource timestamp {resourceChange.Timestamp:O} should be >= checkpoint {checkpoint:O}");
        }

        [Fact]
        public async Task GetRecordsAsync_WithVeryOldDateTime_ReturnsAllRecords()
        {
            // Arrange
            var veryOldCheckpoint = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            // Create a test resource to ensure there's at least some data
            var saveResult = await _fixture.Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));
            var resourceId = saveResult.RawResourceElement.Id;

            var resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(
                _fixture.SqlConnectionWrapperFactory,
                NullLogger<SqlServerFhirResourceChangeDataStore>.Instance,
                _fixture.SchemaInformation);

            // Act - Get records without DateTime filter
            var resultWithoutFilter = await resourceChangeDataStore.GetRecordsAsync(1, 100, CancellationToken.None);

            // Act - Get records with very old DateTime
            var resultWithOldCheckpoint = await resourceChangeDataStore.GetRecordsAsync(1, veryOldCheckpoint, 100, CancellationToken.None);

            // Assert
            Assert.NotNull(resultWithoutFilter);
            Assert.NotNull(resultWithOldCheckpoint);

            // Both should return the same records since the checkpoint is older than any data
            Assert.Equal(resultWithoutFilter.Count, resultWithOldCheckpoint.Count);

            // Verify our test resource is included
            Assert.Contains(resultWithOldCheckpoint, x => x.ResourceId == resourceId);

            // All returned records should have timestamps after the old checkpoint
            Assert.All(resultWithOldCheckpoint, change =>
            {
                Assert.True(
                    change.Timestamp >= veryOldCheckpoint,
                    $"Record timestamp {change.Timestamp:O} should be >= checkpoint {veryOldCheckpoint:O}");
            });
        }

        [Fact]
        public async Task GetRecordsAsync_WithFutureDateTime_ReturnsNoRecords()
        {
            // Arrange
            var futureCheckpoint = DateTime.UtcNow.AddYears(1);

            var resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(
                _fixture.SqlConnectionWrapperFactory,
                NullLogger<SqlServerFhirResourceChangeDataStore>.Instance,
                _fixture.SchemaInformation);

            // Act
            var result = await resourceChangeDataStore.GetRecordsAsync(1, futureCheckpoint, 100, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result); // No records should be after a future date
        }

        [Fact]
        public async Task GetRecordsAsync_MultipleCallsWithSameParameters_ReturnsSameResults()
        {
            // Arrange
            var resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(
                _fixture.SqlConnectionWrapperFactory,
                NullLogger<SqlServerFhirResourceChangeDataStore>.Instance,
                _fixture.SchemaInformation);

            // Act - Call twice with same parameters
            var result1 = await resourceChangeDataStore.GetRecordsAsync(1, 10, CancellationToken.None);
            var result2 = await resourceChangeDataStore.GetRecordsAsync(1, 10, CancellationToken.None);

            // Assert - Should be idempotent (same count and same IDs)
            Assert.Equal(result1.Count, result2.Count);

            if (result1.Count > 0 && result2.Count > 0)
            {
                var ids1 = result1.Select(r => r.Id).OrderBy(id => id).ToList();
                var ids2 = result2.Select(r => r.Id).OrderBy(id => id).ToList();
                Assert.Equal(ids1, ids2);
            }
        }

        [Fact]
        public async Task GetRecordsAsync_AfterResourceCreation_ContainsCorrectResourceChangeType()
        {
            // Arrange
            var saveResult = await _fixture.Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));
            var resourceId = saveResult.RawResourceElement.Id;

            var resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(
                _fixture.SqlConnectionWrapperFactory,
                NullLogger<SqlServerFhirResourceChangeDataStore>.Instance,
                _fixture.SchemaInformation);

            // Act
            var result = await resourceChangeDataStore.GetRecordsAsync(1, 200, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            var changeRecords = result.Where(x => x.ResourceId == resourceId).ToList();
            Assert.NotEmpty(changeRecords);

            // Find the creation record (version 1)
            var creationRecord = changeRecords.FirstOrDefault(x => x.ResourceVersion == 1);
            Assert.NotNull(creationRecord);
            Assert.Equal(0, creationRecord.ResourceChangeTypeId); // 0 = Created
            Assert.Equal("Observation", creationRecord.ResourceTypeName);
        }

        [Fact]
        public async Task GetRecordsAsync_VerifiesResourceTypeIdMapping_IsConsistent()
        {
            // Arrange
            // Create resources of different types
            await _fixture.Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight")); // Observation
            await _fixture.Mediator.UpsertResourceAsync(Samples.GetDefaultOrganization()); // Organization

            var resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(
                _fixture.SqlConnectionWrapperFactory,
                NullLogger<SqlServerFhirResourceChangeDataStore>.Instance,
                _fixture.SchemaInformation);

            // Act
            var result = await resourceChangeDataStore.GetRecordsAsync(1, 200, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.All(result, change =>
            {
                Assert.NotNull(change.ResourceTypeName);
                Assert.NotEmpty(change.ResourceTypeName);
                Assert.True(change.ResourceTypeId > 0);
            });
        }

        [Fact]
        public async Task GetRecordsAsync_WithPagination_CanFetchRecordsInBatches()
        {
            // Arrange
            // Create multiple resources
            for (int i = 0; i < 5; i++)
            {
                await _fixture.Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));
            }

            var resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(
                _fixture.SqlConnectionWrapperFactory,
                NullLogger<SqlServerFhirResourceChangeDataStore>.Instance,
                _fixture.SchemaInformation);

            // Act - Fetch first batch
            var batch1 = await resourceChangeDataStore.GetRecordsAsync(1, 2, CancellationToken.None);

            // Act - Fetch next batch starting from last ID of first batch
            long nextStartId = batch1.Count > 0 ? batch1.Last().Id + 1 : 1;
            var batch2 = await resourceChangeDataStore.GetRecordsAsync(nextStartId, 2, CancellationToken.None);

            // Assert
            Assert.NotNull(batch1);
            Assert.NotNull(batch2);

            // Batches should not overlap
            if (batch1.Count > 0 && batch2.Count > 0)
            {
                var lastIdInBatch1 = batch1.Last().Id;
                var firstIdInBatch2 = batch2.First().Id;
                Assert.True(firstIdInBatch2 > lastIdInBatch1);
            }
        }

        [Fact]
        public async Task GetRecordsAsync_VerifiesTimestampIsUtc()
        {
            // Arrange
            await _fixture.Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));

            var resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(
                _fixture.SqlConnectionWrapperFactory,
                NullLogger<SqlServerFhirResourceChangeDataStore>.Instance,
                _fixture.SchemaInformation);

            // Act
            var result = await resourceChangeDataStore.GetRecordsAsync(1, 10, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.All(result, change =>
            {
                Assert.Equal(DateTimeKind.Utc, change.Timestamp.Kind);
            });
        }

        [Fact]
        public async Task GetRecordsAsync_AfterResourceUpdate_ContainsCorrectResourceChangeType()
        {
            // Arrange
            // Create a resource
            var createResult = await _fixture.Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));
            var resourceId = createResult.RawResourceElement.Id;

            // Update the resource
            var newResourceValues = Samples.GetJsonSample("WeightInGrams").ToPoco();
            newResourceValues.Id = resourceId;
            await _fixture.Mediator.UpsertResourceAsync(newResourceValues.ToResourceElement(), WeakETag.FromVersionId(createResult.RawResourceElement.VersionId));

            var resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(
                _fixture.SqlConnectionWrapperFactory,
                NullLogger<SqlServerFhirResourceChangeDataStore>.Instance,
                _fixture.SchemaInformation);

            // Act
            var result = await resourceChangeDataStore.GetRecordsAsync(1, 200, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            var changeRecords = result.Where(x => x.ResourceId == resourceId).ToList();
            Assert.True(changeRecords.Count >= 2, $"Expected at least 2 change records (create + update), got {changeRecords.Count}");

            // Verify creation record
            var creationRecord = changeRecords.FirstOrDefault(x => x.ResourceVersion == 1);
            Assert.NotNull(creationRecord);
            Assert.Equal(0, creationRecord.ResourceChangeTypeId); // 0 = Created

            // Verify update record
            var updateRecord = changeRecords.FirstOrDefault(x => x.ResourceVersion == 2);
            Assert.NotNull(updateRecord);
            Assert.Equal(1, updateRecord.ResourceChangeTypeId); // 1 = Updated
        }

        [Fact]
        public async Task GetRecordsAsync_WithDateTimeOverload_FiltersCorrectly()
        {
            // Arrange
            var checkpoint = DateTime.UtcNow.AddMinutes(-5);

            // Create a resource after the checkpoint
            var saveResult = await _fixture.Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));
            var resourceId = saveResult.RawResourceElement.Id;

            var resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(
                _fixture.SqlConnectionWrapperFactory,
                NullLogger<SqlServerFhirResourceChangeDataStore>.Instance,
                _fixture.SchemaInformation);

            // Act - Use the DateTime overload
            var result = await resourceChangeDataStore.GetRecordsAsync(1, checkpoint, 100, CancellationToken.None);

            // Assert
            Assert.NotNull(result);

            // Verify that the created resource is in the results
            var resourceChange = result.FirstOrDefault(x => x.ResourceId == resourceId);
            Assert.NotNull(resourceChange);
            Assert.True(resourceChange.Timestamp >= checkpoint, "Resource timestamp should be after the checkpoint");
        }

        [Fact]
        public async Task GetRecordsAsync_VerifiesAllResourceChangeDataFields()
        {
            // Arrange
            var saveResult = await _fixture.Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));
            var resourceId = saveResult.RawResourceElement.Id;

            var resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(
                _fixture.SqlConnectionWrapperFactory,
                NullLogger<SqlServerFhirResourceChangeDataStore>.Instance,
                _fixture.SchemaInformation);

            // Act
            var result = await resourceChangeDataStore.GetRecordsAsync(1, 200, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            var changeRecord = result.FirstOrDefault(x => x.ResourceId == resourceId && x.ResourceVersion == 1);

            Assert.NotNull(changeRecord);
            Assert.True(changeRecord.Id > 0, "Id should be greater than 0");
            Assert.Equal(DateTimeKind.Utc, changeRecord.Timestamp.Kind);
            Assert.Equal(resourceId, changeRecord.ResourceId);
            Assert.True(changeRecord.ResourceTypeId > 0, "ResourceTypeId should be greater than 0");
            Assert.Equal(1, changeRecord.ResourceVersion);
            Assert.InRange(changeRecord.ResourceChangeTypeId, (byte)0, (byte)2); // 0=Created, 1=Updated, 2=Deleted
            Assert.Equal("Observation", changeRecord.ResourceTypeName);
        }
    }
}
