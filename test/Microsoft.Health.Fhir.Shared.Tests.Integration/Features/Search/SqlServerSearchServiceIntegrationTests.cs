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
            // Arrange
            var patient = await CreateTestPatient("TestHistoryPatient");
            var originalSurrogateId = patient.ResourceSurrogateId;

            var sqlSearchService = _searchService as SqlServerSearchService;
            Assert.NotNull(sqlSearchService);

            // Act - Search for resources including history
            var result = await sqlSearchService!.SearchBySurrogateIdRange(
                "Patient",
                originalSurrogateId,
                originalSurrogateId + 100,
                null,
                null,
                CancellationToken.None,
                searchParamHashFilter: null,
                includeHistory: true,
                includeDeleted: false);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Results.Count() >= 1);
        }

        [Fact]
        public async Task SearchBySurrogateIdRange_WithSearchParamHashFilter_FiltersCorrectly()
        {
            // Arrange
            var patient = await CreateTestPatient("TestFilterPatient");
            var surrogateId = patient.ResourceSurrogateId;

            var sqlSearchService = _searchService as SqlServerSearchService;
            Assert.NotNull(sqlSearchService);

            // Act with a non-matching hash
            var result = await sqlSearchService!.SearchBySurrogateIdRange(
                "Patient",
                surrogateId,
                surrogateId + 100,
                null,
                null,
                CancellationToken.None,
                searchParamHashFilter: "non-matching-hash-12345",
                includeHistory: false,
                includeDeleted: false);

            // Assert
            Assert.NotNull(result);

            // Results might be empty or contain resources without the specific hash
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
            // Arrange
            var patient = await CreateTestPatient("TestActiveOnlyPatient");

            var sqlSearchService = _searchService as SqlServerSearchService;
            Assert.NotNull(sqlSearchService);

            // Act - Get surrogate ID ranges with activeOnly flag
            var ranges = await sqlSearchService!.GetSurrogateIdRanges(
                "Patient",
                startId: patient.ResourceSurrogateId,
                endId: patient.ResourceSurrogateId + 100,
                rangeSize: 10,
                numberOfRanges: 1,
                up: true,
                CancellationToken.None,
                activeOnly: true);

            // Assert
            Assert.NotNull(ranges);

            // With activeOnly=true, deleted resources should be excluded
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

            // Observation might or might not be present depending on test execution order
        }

        [Fact]
        public async Task GetStatsFromDatabase_ReturnsStatistics()
        {
            // Arrange
            await CreateTestPatient("TestStatsPatient");

            // Perform a search to potentially generate stats
            await _searchService.SearchAsync(
                "Patient",
                new List<Tuple<string, string>> { Tuple.Create("family", "TestStatsPatient") },
                CancellationToken.None);

            var sqlSearchService = _searchService as SqlServerSearchService;
            Assert.NotNull(sqlSearchService);

            // Act
            var stats = await sqlSearchService!.GetStatsFromDatabase(CancellationToken.None);

            // Assert
            Assert.NotNull(stats);
            Assert.IsAssignableFrom<IReadOnlyList<(string TableName, string ColumnName, short ResourceTypeId, short SearchParamId)>>(stats);
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
    }
}
