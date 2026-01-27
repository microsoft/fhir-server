// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Shared.Tests.Integration.Features.Search
{
    /// <summary>
    /// Integration tests for SqlServerSearchService.
    /// Tests real database interactions for search, surrogate ID operations, and resource type queries.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
    public class SqlServerSearchServiceIntegrationTests : IClassFixture<FhirStorageTestsFixture>, IAsyncLifetime
    {
        private readonly FhirStorageTestsFixture _fixture;
        private readonly ISearchService _searchService;
        private readonly IFhirDataStore _dataStore;
        private readonly List<string> _createdResourceIds = new();

        public SqlServerSearchServiceIntegrationTests(FhirStorageTestsFixture fixture)
        {
            _fixture = fixture;
            _searchService = fixture.SearchService;
            _dataStore = fixture.DataStore;
        }

        public Task InitializeAsync() => Task.CompletedTask;

        public Task DisposeAsync()
        {
            // Clean up is handled by fixture disposal
            return Task.CompletedTask;
        }

        [Fact]
        public async Task SearchBySurrogateIdRange_WithValidRange_ReturnsResources()
        {
            // Arrange
            var patients = await CreateTestPatients(5);
            var surrogateIds = patients.Select(p => p.ResourceSurrogateId).OrderBy(id => id).ToList();

            var sqlSearchService = _searchService as SqlServerSearchService;
            Assert.NotNull(sqlSearchService);

            // Act
            var result = await sqlSearchService!.SearchBySurrogateIdRange(
                "Patient",
                surrogateIds.First(),
                surrogateIds.Last(),
                null,
                null,
                CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.NotNull(result.Results);
            Assert.True(result.Results.Count() >= 1, $"Expected at least 1 result, got {result.Results.Count()}");
            Assert.All(result.Results, r =>
            {
                Assert.Equal("Patient", r.Resource.ResourceTypeName);
                Assert.True(r.Resource.ResourceSurrogateId >= surrogateIds.First());
                Assert.True(r.Resource.ResourceSurrogateId <= surrogateIds.Last());
            });
        }

        [Fact]
        public async Task SearchBySurrogateIdRange_WithIncludeHistory_ReturnsHistoricalVersions()
        {
            // Arrange - Create an observation and then update it to create historical versions
            // Note: Using Observation because it's configured with Versioned policy (keeps history)
            var observationWrapper = await CreateTestObservation("TestHistoryObservation");
            var originalSurrogateId = observationWrapper.ResourceSurrogateId;
            var resourceId = observationWrapper.ResourceId;

            // Update the observation to create version 2 (making version 1 historical)
            var observationElement = new RawResourceElement(observationWrapper);
            var observationToUpdate = observationElement.ToPoco<Observation>(_fixture.Deserializer);
            var updatedResource = UpdateObservationStatus(observationToUpdate);
            await _fixture.Mediator.UpsertResourceAsync(updatedResource, WeakETag.FromVersionId(observationWrapper.Version));

            // Update again to create version 3 (making versions 1 and 2 historical)
            var observationWrapper2 = await _dataStore.GetAsync(new ResourceKey("Observation", resourceId), CancellationToken.None);
            var observationElement2 = new RawResourceElement(observationWrapper2);
            var observationToUpdate2 = observationElement2.ToPoco<Observation>(_fixture.Deserializer);
            var updatedResource2 = UpdateObservationStatus(observationToUpdate2);
            await _fixture.Mediator.UpsertResourceAsync(updatedResource2, WeakETag.FromVersionId(observationWrapper2.Version));

            // Get the final wrapper to have the latest surrogate ID
            var finalWrapper = await _dataStore.GetAsync(new ResourceKey("Observation", resourceId), CancellationToken.None);

            var sqlSearchService = _searchService as SqlServerSearchService;
            Assert.NotNull(sqlSearchService);

            // Act - Search for resources including history
            // Use a wide range from the first surrogate ID to well beyond the last
            var result = await sqlSearchService!.SearchBySurrogateIdRange(
                "Observation",
                originalSurrogateId,
                finalWrapper.ResourceSurrogateId + 100,
                null,
                null,
                CancellationToken.None,
                searchParamHashFilter: null,
                includeHistory: true,
                includeDeleted: false);

            // Assert - Should return multiple versions of the same resource
            Assert.NotNull(result);
            var resultsList = result.Results.ToList();

            // Debug output to see what we got
            var versionsFound = resultsList
                .Where(r => r.Resource.ResourceId == resourceId)
                .Select(r => $"Version {r.Resource.Version}, IsHistory={r.Resource.IsHistory}, SurrogateId={r.Resource.ResourceSurrogateId}")
                .ToList();

            Assert.True(
                resultsList.Count >= 2,
                $"Expected at least 2 historical versions, got {resultsList.Count}. Versions found for resource {resourceId}: [{string.Join(", ", versionsFound)}]");

            // Verify that we have multiple versions of the same resource ID
            var resourcesWithSameId = resultsList.Where(r => r.Resource.ResourceId == resourceId).ToList();
            Assert.True(
                resourcesWithSameId.Count >= 2,
                $"Expected multiple versions of resource {resourceId}, got {resourcesWithSameId.Count}. Versions: [{string.Join(", ", versionsFound)}]");

            // Verify that the versions are different
            var versions = resourcesWithSameId.Select(r => r.Resource.Version).Distinct().ToList();
            Assert.True(
                versions.Count >= 2,
                $"Expected different version numbers for historical versions, got {versions.Count} unique versions: [{string.Join(", ", versions)}]");
        }

        [Fact]
        public async Task GetSurrogateIdRanges_ReturnsValidRanges()
        {
            // Arrange
            await CreateTestPatients(20);

            var sqlSearchService = _searchService as SqlServerSearchService;
            Assert.NotNull(sqlSearchService);

            // Act
            var ranges = await sqlSearchService!.GetSurrogateIdRanges(
                "Patient",
                startId: 1,
                endId: long.MaxValue,
                rangeSize: 5,
                numberOfRanges: 3,
                up: true,
                CancellationToken.None);

            // Assert
            Assert.NotNull(ranges);
            Assert.All(ranges, range =>
            {
                Assert.True(range.StartId > 0);
                Assert.True(range.EndId >= range.StartId);
                Assert.True(range.Count >= 0);
            });
        }

        [Fact]
        public async Task GetSurrogateIdRanges_WithActiveOnlyFlag_ReturnsOnlyActiveResources()
        {
            // Arrange - Create multiple patients
            var patients = await CreateTestPatients(5);
            var surrogateIds = patients.Select(p => p.ResourceSurrogateId).OrderBy(id => id).ToList();

            // Soft delete some of the patients (e.g., the first 2)
            // Note: Soft delete creates a new version with IsDeleted=1
            // Whether history is kept depends on the resource type's configuration
            for (int i = 0; i < 2; i++)
            {
                await _fixture.Mediator.DeleteResourceAsync(
                    new ResourceKey("Patient", patients[i].ResourceId),
                    DeleteOperation.SoftDelete,
                    CancellationToken.None);
            }

            var sqlSearchService = _searchService as SqlServerSearchService;
            Assert.NotNull(sqlSearchService);

            // Determine the new maximum surrogate ID after deletes (which create new versions)
            var maxSurrogateId = surrogateIds.Last();

            // Fetch one of the deleted patients to get its latest surrogate ID
            var firstDeletedPatient = await _dataStore.GetAsync(new ResourceKey("Patient", patients[0].ResourceId), CancellationToken.None);
            if (firstDeletedPatient.ResourceSurrogateId > maxSurrogateId)
            {
                maxSurrogateId = firstDeletedPatient.ResourceSurrogateId;
            }

            // Act - Get surrogate ID ranges with activeOnly=true (should exclude deleted resources)
            var rangesActiveOnly = await sqlSearchService!.GetSurrogateIdRanges(
                "Patient",
                startId: surrogateIds.First(),
                endId: maxSurrogateId + 100,
                rangeSize: 20,
                numberOfRanges: 10,
                up: true,
                CancellationToken.None,
                activeOnly: true);

            // Act - Get surrogate ID ranges with activeOnly=false (should include all resources)
            var rangesAll = await sqlSearchService!.GetSurrogateIdRanges(
                "Patient",
                startId: surrogateIds.First(),
                endId: maxSurrogateId + 100,
                rangeSize: 20,
                numberOfRanges: 10,
                up: true,
                CancellationToken.None,
                activeOnly: false);

            // Assert - With activeOnly=true, should only include active (non-deleted, non-history) resources
            Assert.NotNull(rangesActiveOnly);
            var totalActiveCount = rangesActiveOnly.Sum(r => r.Count);

            // Should have exactly 3 active resources (5 created - 2 deleted)
            Assert.Equal(3, totalActiveCount);

            // Assert - With activeOnly=false, should include all resource versions
            Assert.NotNull(rangesAll);
            var totalAllCount = rangesAll.Sum(r => r.Count);

            // The key assertion: activeOnly=false MUST return more resources than activeOnly=true
            // because it includes deleted resources that activeOnly=true filters out
            Assert.True(
                totalAllCount > totalActiveCount,
                $"Expected total count ({totalAllCount}) to be > active count ({totalActiveCount})");

            // Additional assertion: We must have at least 1 deleted resource showing up in the all count
            // Since we deleted 2 resources, we expect at least 4 total (3 active + at least 1 deleted)
            Assert.True(
                totalAllCount >= 4,
                $"Expected at least 4 resource versions (3 active + at least 1 deleted), got {totalAllCount}");
        }

        [Fact]
        public async Task GetUsedResourceTypes_ReturnsExistingTypes()
        {
            // Arrange
            await CreateTestPatient("TestResourceTypesPatient");
            await CreateTestObservation("TestResourceTypesObservation");

            var sqlSearchService = _searchService as SqlServerSearchService;
            Assert.NotNull(sqlSearchService);

            // Act
            var resourceTypes = await sqlSearchService!.GetUsedResourceTypes(CancellationToken.None);

            // Assert
            Assert.NotNull(resourceTypes);
            Assert.Contains("Patient", resourceTypes);
            Assert.Contains("Observation", resourceTypes);
        }

        [Fact]
        public async Task GetStatsFromDatabase_ReturnsStatistics()
        {
            // Arrange
            var sqlSearchService = _searchService as SqlServerSearchService;
            Assert.NotNull(sqlSearchService);

            // Act
            var stats = await sqlSearchService!.GetStatsFromDatabase(CancellationToken.None);

            // Assert
            Assert.NotNull(stats);

            // If statistics exist, verify they have the expected structure
            if (stats.Any())
            {
                var firstStat = stats.First();
                Assert.False(string.IsNullOrEmpty(firstStat.TableName));
                Assert.False(string.IsNullOrEmpty(firstStat.ColumnName));
            }
        }

        [Fact]
        public void GetStatsFromCache_ReturnsCollection()
        {
            // Act
            var stats = SqlServerSearchService.GetStatsFromCache();

            // Assert
            Assert.NotNull(stats);
            Assert.IsAssignableFrom<ICollection<(string TableName, string ColumnName, short ResourceTypeId, short SearchParamId)>>(stats);
        }

        [Fact]
        public void ResetReuseQueryPlans_ExecutesWithoutError()
        {
            // Act & Assert (should not throw)
            SqlServerSearchService.ResetReuseQueryPlans();
        }

        [Fact]
        public void StoredProcedureLayerIsEnabled_CanBeToggled()
        {
            // Arrange
            var sqlSearchService = _searchService as SqlServerSearchService;
            Assert.NotNull(sqlSearchService);

            var originalValue = sqlSearchService!.StoredProcedureLayerIsEnabled;

            // Act
            sqlSearchService!.StoredProcedureLayerIsEnabled = !originalValue;

            // Assert
            Assert.Equal(!originalValue, sqlSearchService!.StoredProcedureLayerIsEnabled);

            // Cleanup - restore original value
            sqlSearchService!.StoredProcedureLayerIsEnabled = originalValue;
        }

        [Fact]
        public async Task SearchBySurrogateIdRange_WithEmptyRange_ReturnsEmptyResults()
        {
            // Arrange
            var sqlSearchService = _searchService as SqlServerSearchService;
            Assert.NotNull(sqlSearchService);

            // Act - Search in a range where no resources exist
            var result = await sqlSearchService!.SearchBySurrogateIdRange(
                "Patient",
                999999999,
                999999999,
                null,
                null,
                CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result.Results);
            Assert.Equal(0, result.TotalCount);
        }

        [Fact]
        public async Task GetSurrogateIdRanges_WithSmallRangeSize_ReturnsMultipleRanges()
        {
            // Arrange
            await CreateTestPatients(15);

            var sqlSearchService = _searchService as SqlServerSearchService;
            Assert.NotNull(sqlSearchService);

            // Act
            var ranges = await sqlSearchService!.GetSurrogateIdRanges(
                "Patient",
                startId: 1,
                endId: long.MaxValue,
                rangeSize: 3,
                numberOfRanges: 5,
                up: true,
                CancellationToken.None);

            // Assert
            Assert.NotNull(ranges);

            // Should return multiple ranges with small range size
            // We expect at least 2 ranges (since we have 15 resources with range size of 3)
            Assert.True(ranges.Count() >= 2, $"Expected at least 2 ranges, got {ranges.Count()}");

            // Should not exceed the requested number of ranges
            Assert.True(ranges.Count() <= 5, $"Expected at most 5 ranges, got {ranges.Count()}");

            // Verify that each range has a reasonable count given the range size
            Assert.All(ranges, range => Assert.True(range.Count <= 3, $"Expected range count <= 3, got {range.Count}"));
        }

        private async Task<List<ResourceWrapper>> CreateTestPatients(int count)
        {
            var patients = new List<ResourceWrapper>();

            for (int i = 0; i < count; i++)
            {
                var patient = await CreateTestPatient($"TestPatient{i}_{Guid.NewGuid()}");
                patients.Add(patient);
            }

            return patients;
        }

        private async Task<ResourceWrapper> CreateTestPatient(string familyName)
        {
            var patient = new Patient
            {
                Name = new List<HumanName>
                {
                    new HumanName { Family = familyName, Given = new[] { "Test" } },
                },
                Gender = AdministrativeGender.Unknown,
            };

            var saveResult = await _fixture.Mediator.UpsertResourceAsync(patient.ToResourceElement());
            var wrapper = await _dataStore.GetAsync(new ResourceKey("Patient", saveResult.RawResourceElement.Id), CancellationToken.None);
            _createdResourceIds.Add($"Patient/{wrapper.ResourceId}");

            return wrapper;
        }

        private async Task<ResourceWrapper> CreateTestObservation(string display)
        {
            var observation = new Observation
            {
                Status = ObservationStatus.Final,
                Code = new CodeableConcept { Text = display },
            };

            var saveResult = await _fixture.Mediator.UpsertResourceAsync(observation.ToResourceElement());
            var wrapper = await _dataStore.GetAsync(new ResourceKey("Observation", saveResult.RawResourceElement.Id), CancellationToken.None);
            _createdResourceIds.Add($"Observation/{wrapper.ResourceId}");

            return wrapper;
        }

        private ResourceElement UpdatePatientGender(Patient patient)
        {
            patient.Gender = patient.Gender == AdministrativeGender.Male
                ? AdministrativeGender.Female
                : AdministrativeGender.Male;

            return patient.ToResourceElement();
        }

        private ResourceElement UpdateObservationStatus(Observation observation)
        {
            observation.Status = observation.Status == ObservationStatus.Final
                ? ObservationStatus.Amended
                : ObservationStatus.Final;

            return observation.ToResourceElement();
        }
    }
}
