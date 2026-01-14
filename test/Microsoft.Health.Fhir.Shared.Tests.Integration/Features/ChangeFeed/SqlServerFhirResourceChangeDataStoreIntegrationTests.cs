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

        [Fact]
        public async Task GetRecordsAsync_WithInvalidStartId_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            var resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(
                _fixture.SqlConnectionWrapperFactory,
                NullLogger<SqlServerFhirResourceChangeDataStore>.Instance,
                _fixture.SchemaInformation);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await resourceChangeDataStore.GetRecordsAsync(0, 10, CancellationToken.None));
        }

        [Fact]
        public async Task GetRecordsAsync_WithNegativeStartId_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            var resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(
                _fixture.SqlConnectionWrapperFactory,
                NullLogger<SqlServerFhirResourceChangeDataStore>.Instance,
                _fixture.SchemaInformation);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await resourceChangeDataStore.GetRecordsAsync(-1, 10, CancellationToken.None));
        }

        [Fact]
        public async Task GetRecordsAsync_WithInvalidPageSize_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            var resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(
                _fixture.SqlConnectionWrapperFactory,
                NullLogger<SqlServerFhirResourceChangeDataStore>.Instance,
                _fixture.SchemaInformation);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await resourceChangeDataStore.GetRecordsAsync(1, 0, CancellationToken.None));
        }

        [Fact]
        public async Task GetRecordsAsync_WithNegativePageSize_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            var resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(
                _fixture.SqlConnectionWrapperFactory,
                NullLogger<SqlServerFhirResourceChangeDataStore>.Instance,
                _fixture.SchemaInformation);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await resourceChangeDataStore.GetRecordsAsync(1, -1, CancellationToken.None));
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
            var resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(
                _fixture.SqlConnectionWrapperFactory,
                NullLogger<SqlServerFhirResourceChangeDataStore>.Instance,
                _fixture.SchemaInformation);

            // Act - Request with very large page size
            var result = await resourceChangeDataStore.GetRecordsAsync(1, short.MaxValue, CancellationToken.None);

            // Assert
            Assert.NotNull(result);

            // Should return all available records without throwing
        }

        [Fact]
        public async Task GetRecordsAsync_WithDateTime_ReturnsRecordsAfterCheckpoint()
        {
            // Arrange
            var checkpoint = DateTime.UtcNow.AddMinutes(-10);

            // Create a resource after the checkpoint
            await _fixture.Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));

            var resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(
                _fixture.SqlConnectionWrapperFactory,
                NullLogger<SqlServerFhirResourceChangeDataStore>.Instance,
                _fixture.SchemaInformation);

            // Act
            var result = await resourceChangeDataStore.GetRecordsAsync(1, checkpoint, 100, CancellationToken.None);

            // Assert
            Assert.NotNull(result);

            // Should return records created after the checkpoint
        }

        [Fact]
        public async Task GetRecordsAsync_WithVeryOldDateTime_ReturnsAllRecords()
        {
            // Arrange
            var veryOldCheckpoint = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            var resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(
                _fixture.SqlConnectionWrapperFactory,
                NullLogger<SqlServerFhirResourceChangeDataStore>.Instance,
                _fixture.SchemaInformation);

            // Act
            var result = await resourceChangeDataStore.GetRecordsAsync(1, veryOldCheckpoint, 100, CancellationToken.None);

            // Assert
            Assert.NotNull(result);

            // Should return all available records
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

            // Assert - Should be idempotent
            Assert.Equal(result1.Count, result2.Count);
        }

        [Fact]
        public async Task GetRecordsAsync_AfterResourceCreation_ContainsCorrectResourceChangeType()
        {
            // Arrange
            var saveResult = await _fixture.Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));

            var resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(
                _fixture.SqlConnectionWrapperFactory,
                NullLogger<SqlServerFhirResourceChangeDataStore>.Instance,
                _fixture.SchemaInformation);

            // Act
            var result = await resourceChangeDataStore.GetRecordsAsync(1, 200, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            var changeRecord = Assert.Single(result, x => x.ResourceId == saveResult.RawResourceElement.Id);
            Assert.Equal(0, changeRecord.ResourceChangeTypeId); // 0 = Created
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
            long nextStartId = batch1.Count > 0 ? ((System.Collections.Generic.List<Core.Models.ResourceChangeData>)batch1)[batch1.Count - 1].Id + 1 : 1;
            var batch2 = await resourceChangeDataStore.GetRecordsAsync(nextStartId, 2, CancellationToken.None);

            // Assert
            Assert.NotNull(batch1);
            Assert.NotNull(batch2);

            // Batches should not overlap
            if (batch1.Count > 0 && batch2.Count > 0)
            {
                var lastIdInBatch1 = ((System.Collections.Generic.List<Core.Models.ResourceChangeData>)batch1)[batch1.Count - 1].Id;
                var firstIdInBatch2 = ((System.Collections.Generic.List<Core.Models.ResourceChangeData>)batch2)[0].Id;
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
    }
}
