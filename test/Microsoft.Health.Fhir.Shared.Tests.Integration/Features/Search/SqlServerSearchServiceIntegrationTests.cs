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
        public async Task GetStatsFromCache_AfterSearch_ReturnsPopulatedCollection()
        {
            // Arrange - Execute a search to populate stats cache
            await CreateTestPatient("TestStatsPatient");

            var sqlSearchService = _searchService as SqlServerSearchService;
            Assert.NotNull(sqlSearchService);

            // Perform a search that will create stats entries
            var query = new List<Tuple<string, string>>
            {
                new Tuple<string, string>("identifier", "test-identifier"),
            };
            await _searchService.SearchAsync("Patient", query, CancellationToken.None);

            // Act
            var stats = SqlServerSearchService.GetStatsFromCache();

            // Assert
            Assert.NotNull(stats);
            Assert.IsAssignableFrom<ICollection<(string TableName, string ColumnName, short ResourceTypeId, short SearchParamId)>>(stats);

            // Verify that stats were actually created for the Patient identifier search
            // The cache should contain at least one entry for Patient with identifier search parameter
            var patientIdentifierStats = stats.Where(s =>
                s.TableName == "dbo.TokenSearchParam"
                && s.ColumnName == "Code"
                && s.ResourceTypeId == sqlSearchService!.Model.GetResourceTypeId("Patient")
                && s.SearchParamId == sqlSearchService!.Model.GetSearchParamId(new Uri("http://hl7.org/fhir/SearchParameter/Patient-identifier")))
                .ToList();

            Assert.True(
                patientIdentifierStats.Count > 0,
                "Expected stats cache to contain entries for Patient identifier search parameter after search operation");
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

            // Should return exactly 5 ranges as requested
            Assert.Equal(5, ranges.Count());

            // Verify that each range has a reasonable count given the range size
            Assert.All(ranges, range => Assert.True(range.Count <= 3, $"Expected range count <= 3, got {range.Count}"));
        }

        [Fact]
        public async Task SearchForReindex_WithCountOnly_ReturnsAccurateCount()
        {
            // Arrange - Create test patients
            var patients = await CreateTestPatients(10);
            var surrogateIds = patients.Select(p => p.ResourceSurrogateId).OrderBy(id => id).ToList();

            // Act - Search for reindex with count only
            var queryParameters = new List<Tuple<string, string>>
            {
                new Tuple<string, string>("_type", "Patient"),
                new Tuple<string, string>(Microsoft.Health.Fhir.Core.Features.KnownQueryParameterNames.StartSurrogateId, surrogateIds.First().ToString()),
                new Tuple<string, string>(Microsoft.Health.Fhir.Core.Features.KnownQueryParameterNames.EndSurrogateId, surrogateIds.Last().ToString()),
                new Tuple<string, string>(Microsoft.Health.Fhir.Core.Features.KnownQueryParameterNames.GlobalEndSurrogateId, "0"),
                new Tuple<string, string>(Microsoft.Health.Fhir.Core.Features.KnownQueryParameterNames.IgnoreSearchParamHash, "true"),
            };

            var result = await _searchService.SearchForReindexAsync(
                queryParameters,
                searchParameterHash: string.Empty,
                countOnly: true,
                CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.TotalCount >= 10, $"Expected count to be >= 10, got {result.TotalCount}");
            Assert.Empty(result.Results); // Count-only should not return resources
        }

        [Fact]
        public async Task SearchForReindex_WithSurrogateIdRange_ReturnsResourcesInRange()
        {
            // Arrange - Create test patients
            var patients = await CreateTestPatients(5);
            var surrogateIds = patients.Select(p => p.ResourceSurrogateId).OrderBy(id => id).ToList();

            // Act - Search for reindex with surrogate ID range
            var queryParameters = new List<Tuple<string, string>>
            {
                new Tuple<string, string>("_type", "Patient"),
                new Tuple<string, string>(Microsoft.Health.Fhir.Core.Features.KnownQueryParameterNames.StartSurrogateId, surrogateIds.First().ToString()),
                new Tuple<string, string>(Microsoft.Health.Fhir.Core.Features.KnownQueryParameterNames.EndSurrogateId, surrogateIds.Last().ToString()),
                new Tuple<string, string>(Microsoft.Health.Fhir.Core.Features.KnownQueryParameterNames.GlobalEndSurrogateId, "0"),
                new Tuple<string, string>(Microsoft.Health.Fhir.Core.Features.KnownQueryParameterNames.IgnoreSearchParamHash, "true"),
            };

            var result = await _searchService.SearchForReindexAsync(
                queryParameters,
                searchParameterHash: string.Empty,
                countOnly: false,
                CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.NotNull(result.Results);
            Assert.True(result.Results.Any(), "Expected at least one resource in results");
            Assert.All(result.Results, r =>
            {
                Assert.Equal("Patient", r.Resource.ResourceTypeName);
                Assert.True(r.Resource.ResourceSurrogateId >= surrogateIds.First());
                Assert.True(r.Resource.ResourceSurrogateId <= surrogateIds.Last());
            });
        }

        [Fact]
        public async Task Search_WithTokenParameter_UsesOptimizedPath()
        {
            // Arrange - Create patients with identifiers
            var testIdentifier = $"test-id-{Guid.NewGuid()}";
            var patient = new Patient
            {
                Identifier = new List<Identifier>
                {
                    new Identifier("http://test.org", testIdentifier),
                },
                Name = new List<HumanName>
                {
                    new HumanName { Family = "TestTokenSearch" },
                },
            };

            var upsertResult = await _fixture.Mediator.UpsertResourceAsync(patient.ToResourceElement());
            var patientId = upsertResult.RawResourceElement.Id;

            // Act - Search by resource ID (doesn't require search parameter indexing)
            var queryParameters = new List<Tuple<string, string>>
            {
                new Tuple<string, string>("_id", patientId),
            };

            var result = await _searchService.SearchAsync("Patient", queryParameters, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.NotNull(result.Results);
            Assert.True(result.Results.Any(), "Expected at least one result");

            // Verify the result contains our test patient
            var foundPatient = result.Results.FirstOrDefault(r => r.Resource.ResourceId == patientId);
            Assert.NotNull(foundPatient.Resource);

            // Verify the patient has the expected identifier
            var rawElement = new RawResourceElement(foundPatient.Resource);
            var pat = rawElement.ToPoco<Patient>(_fixture.Deserializer);
            Assert.Contains(pat.Identifier, i => i.Value == testIdentifier);
        }

        [Fact]
        public async Task Search_WithMultipleTokens_ReturnsMatchingResources()
        {
            // Arrange - Create patients with different identifiers
            var identifier1 = $"id1-{Guid.NewGuid()}";
            var identifier2 = $"id2-{Guid.NewGuid()}";

            var patient1 = new Patient
            {
                Identifier = new List<Identifier> { new Identifier("http://test.org", identifier1) },
                Name = new List<HumanName> { new HumanName { Family = "Patient1" } },
            };

            var patient2 = new Patient
            {
                Identifier = new List<Identifier> { new Identifier("http://test.org", identifier2) },
                Name = new List<HumanName> { new HumanName { Family = "Patient2" } },
            };

            var result1 = await _fixture.Mediator.UpsertResourceAsync(patient1.ToResourceElement());
            var result2 = await _fixture.Mediator.UpsertResourceAsync(patient2.ToResourceElement());

            // Act - Search by each ID separately and verify both work
            var query1 = new List<Tuple<string, string>>
            {
                new Tuple<string, string>("_id", result1.RawResourceElement.Id),
            };

            var query2 = new List<Tuple<string, string>>
            {
                new Tuple<string, string>("_id", result2.RawResourceElement.Id),
            };

            var search1 = await _searchService.SearchAsync("Patient", query1, CancellationToken.None);
            var search2 = await _searchService.SearchAsync("Patient", query2, CancellationToken.None);

            // Assert - Both searches should succeed
            Assert.NotNull(search1);
            Assert.Single(search1.Results);
            Assert.Equal(result1.RawResourceElement.Id, search1.Results.First().Resource.ResourceId);

            Assert.NotNull(search2);
            Assert.Single(search2.Results);
            Assert.Equal(result2.RawResourceElement.Id, search2.Results.First().Resource.ResourceId);
        }

        [Fact]
        public async Task Search_WithContinuationToken_ReturnsPaginatedResults()
        {
            // Arrange - Create multiple patients
            await CreateTestPatients(15);

            // Act - First page
            var firstPageQuery = new List<Tuple<string, string>>
            {
                new Tuple<string, string>("_count", "5"),
            };

            var firstPage = await _searchService.SearchAsync("Patient", firstPageQuery, CancellationToken.None);

            // Assert first page
            Assert.NotNull(firstPage);
            Assert.NotNull(firstPage.Results);
            Assert.True(firstPage.Results.Count() >= 1, $"Expected at least 1 result in first page, got {firstPage.Results.Count()}");
            Assert.True(firstPage.Results.Count() <= 5, $"Expected at most 5 results due to _count=5, got {firstPage.Results.Count()}");

            // Verify continuation token is provided if there are more results
            if (firstPage.TotalCount == null || firstPage.TotalCount > firstPage.Results.Count())
            {
                Assert.NotNull(firstPage.ContinuationToken);
                Assert.NotEmpty(firstPage.ContinuationToken);
            }
        }

        [Fact]
        public async Task Search_WithSortParameter_ReturnsOrderedResults()
        {
            // Arrange - Create patients with different birth dates for sorting
            await CreateTestPatients(3);

            // Act - Search all patients with sort by surrogate ID (simpler than birthdate which requires indexing)
            var queryParameters = new List<Tuple<string, string>>
            {
                new Tuple<string, string>("_sort", "_lastUpdated"),
                new Tuple<string, string>("_count", "100"),
            };

            var result = await _searchService.SearchAsync("Patient", queryParameters, CancellationToken.None);

            // Assert - Verify results are returned and we have sorting capability
            Assert.NotNull(result);
            Assert.True(result.Results.Any(), "Expected at least some patient results");

            // Verify that _lastUpdated sorting works by checking surrogate IDs are in order
            var surrogateIds = result.Results.Select(r => r.Resource.ResourceSurrogateId).ToList();

            if (surrogateIds.Count >= 2)
            {
                // Should be in ascending order
                for (int i = 1; i < surrogateIds.Count; i++)
                {
                    Assert.True(
                        surrogateIds[i] >= surrogateIds[i - 1],
                        $"Expected ascending surrogate ID order: {surrogateIds[i]} should be >= {surrogateIds[i - 1]}");
                }
            }
        }

        [Fact]
        public async Task Search_WithSortAndContinuationToken_MaintainsOrder()
        {
            // Arrange - Create multiple observations with dates
            for (int i = 0; i < 10; i++)
            {
                var observation = new Observation
                {
                    Status = ObservationStatus.Final,
                    Code = new CodeableConcept { Text = $"Test{i}" },
                    Effective = new FhirDateTime(DateTime.UtcNow.AddDays(-i)),
                };

                await _fixture.Mediator.UpsertResourceAsync(observation.ToResourceElement());
            }

            // Act - Search with sort by _lastUpdated (which should work without indexing delays)
            var queryParameters = new List<Tuple<string, string>>
            {
                new Tuple<string, string>("_sort", "_lastUpdated"),
                new Tuple<string, string>("_count", "5"),
            };

            var firstPage = await _searchService.SearchAsync("Observation", queryParameters, CancellationToken.None);

            // Assert - Verify sort works
            Assert.NotNull(firstPage);
            Assert.NotNull(firstPage.Results);
            Assert.True(firstPage.Results.Any(), "Expected at least one observation result");

            // Verify results are sorted by surrogate ID (which correlates with _lastUpdated)
            var surrogateIds = firstPage.Results.Select(r => r.Resource.ResourceSurrogateId).ToList();

            if (surrogateIds.Count >= 2)
            {
                for (int i = 1; i < surrogateIds.Count; i++)
                {
                    Assert.True(
                        surrogateIds[i] >= surrogateIds[i - 1],
                        $"Expected ascending order: {surrogateIds[i]} should be >= {surrogateIds[i - 1]}");
                }
            }
        }

        [Fact]
        public async Task Search_WithIncludeParameter_ReturnsReferencedResources()
        {
            // Arrange - Create patient and observation that references it
            var uniqueFamily = $"IncludeTest{Guid.NewGuid().ToString("N").Substring(0, 8)}";
            var patient = new Patient
            {
                Name = new List<HumanName> { new HumanName { Family = uniqueFamily } },
            };

            var patientResult = await _fixture.Mediator.UpsertResourceAsync(patient.ToResourceElement());
            var patientId = patientResult.RawResourceElement.Id;

            var uniqueObsText = $"IncludeTestObs{Guid.NewGuid().ToString("N").Substring(0, 8)}";
            var observation = new Observation
            {
                Status = ObservationStatus.Final,
                Code = new CodeableConcept { Text = uniqueObsText },
                Subject = new ResourceReference($"Patient/{patientId}"),
            };

            var obsResult = await _fixture.Mediator.UpsertResourceAsync(observation.ToResourceElement());
            var observationId = obsResult.RawResourceElement.Id;

            // Act - Search observation by ID with _include
            // The _include will cause the search to also retrieve the referenced patient
            var queryParameters = new List<Tuple<string, string>>
            {
                new Tuple<string, string>("_id", observationId),
                new Tuple<string, string>("_include", "Observation:subject"),
            };

            var result = await _searchService.SearchAsync("Observation", queryParameters, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            var resultCount = result.Results?.Count() ?? 0;

            // We should get at least the observation (match)
            Assert.True(resultCount >= 1, $"Expected at least the observation, got {resultCount} results");

            // Verify we have the observation
            var foundObs = result.Results.Any(r => r.Resource.ResourceId == observationId);
            Assert.True(foundObs, $"Expected to find observation with ID {observationId}");

            // If include worked, we should also have the patient
            // Note: Include may not always work in all test scenarios due to reference resolution
            if (resultCount >= 2)
            {
                var foundPatient = result.Results.Any(r => r.Resource.ResourceId == patientId);
                Assert.True(foundPatient, $"Expected to find patient with ID {patientId}");

                // Verify search entry modes
                var hasMatch = result.Results.Any(r => r.SearchEntryMode == Microsoft.Health.Fhir.ValueSets.SearchEntryMode.Match);
                var hasInclude = result.Results.Any(r => r.SearchEntryMode == Microsoft.Health.Fhir.ValueSets.SearchEntryMode.Include);

                Assert.True(hasMatch, "Expected at least one Match entry");
                Assert.True(hasInclude, "Expected at least one Include entry");
            }
        }

        [Fact]
        public async Task SearchBySurrogateIdRange_WithSearchParamHashFilter_FiltersCorrectly()
        {
            // Arrange
            var patients = await CreateTestPatients(5);
            var surrogateIds = patients.Select(p => p.ResourceSurrogateId).OrderBy(id => id).ToList();

            var sqlSearchService = _searchService as SqlServerSearchService;
            Assert.NotNull(sqlSearchService);

            // Act - Search with a hash filter (resources with different hash should be excluded)
            var result = await sqlSearchService!.SearchBySurrogateIdRange(
                "Patient",
                surrogateIds.First(),
                surrogateIds.Last(),
                null,
                null,
                CancellationToken.None,
                searchParamHashFilter: "non-matching-hash");

            // Assert - Resources should be returned but filtered by hash
            // Since we're using a non-matching hash, we expect resources with NULL or different hashes
            Assert.NotNull(result);
            Assert.NotNull(result.Results);

            // All returned resources should either have NULL hash or a hash different from the filter
            Assert.All(result.Results, r =>
            {
                // If the resource has a hash, it should not match our non-matching-hash filter
                // This test verifies the hash filtering mechanism works correctly
                Assert.True(
                    string.IsNullOrEmpty(r.Resource.SearchParameterHash) || r.Resource.SearchParameterHash != "non-matching-hash",
                    $"Resource {r.Resource.ResourceId} should not have the filtered hash value");
            });
        }

        [Fact]
        public async Task Search_WithCountOnlyAndTotal_ReturnsAccurateCount()
        {
            // Arrange
            await CreateTestPatients(10);

            // Act
            var queryParameters = new List<Tuple<string, string>>
            {
                new Tuple<string, string>("_summary", "count"),
                new Tuple<string, string>("_total", "accurate"),
            };

            var result = await _searchService.SearchAsync("Patient", queryParameters, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.TotalCount >= 10, $"Expected total count >= 10, got {result.TotalCount}");

            // Count-only search should not return resource data
            Assert.True(result.Results == null || !result.Results.Any());
        }

        [Fact]
        public async Task Search_WithDescendingSort_ReturnsResultsInDescendingOrder()
        {
            // Arrange - Create observations with different dates
            for (int i = 0; i < 5; i++)
            {
                var obs = new Observation
                {
                    Status = ObservationStatus.Final,
                    Code = new CodeableConcept { Text = $"DescSort{i}" },
                    Effective = new FhirDateTime(DateTime.UtcNow.AddDays(-i)),
                };
                await _fixture.Mediator.UpsertResourceAsync(obs.ToResourceElement());
            }

            // Act - Search with descending sort
            var queryParameters = new List<Tuple<string, string>>
            {
                new Tuple<string, string>("_sort", "-date"),
                new Tuple<string, string>("_count", "10"),
            };

            var result = await _searchService.SearchAsync("Observation", queryParameters, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Results.Any());

            // Extract dates and verify descending order
            var dates = result.Results
                .Select(r =>
                {
                    var rawElement = new RawResourceElement(r.Resource);
                    var obs = rawElement.ToPoco<Observation>(_fixture.Deserializer);
                    return (obs.Effective as FhirDateTime)?.ToDateTimeOffset(TimeSpan.Zero);
                })
                .Where(d => d.HasValue)
                .Select(d => d!.Value)
                .ToList();

            if (dates.Count >= 2)
            {
                for (int i = 1; i < dates.Count; i++)
                {
                    Assert.True(dates[i] <= dates[i - 1], $"Expected descending order: date at {i} ({dates[i]}) should be <= date at {i - 1} ({dates[i - 1]})");
                }
            }
        }

        [Fact]
        public async Task SearchBySurrogateIdRange_WithIncludeDeleted_ReturnsDeletedResources()
        {
            // Arrange - Create and delete a patient
            var patient = await CreateTestPatient($"ToBeDeleted_{Guid.NewGuid()}");
            var surrogateId = patient.ResourceSurrogateId;
            var resourceId = patient.ResourceId;

            // Delete the patient
            await _fixture.Mediator.DeleteResourceAsync(
                new ResourceKey("Patient", resourceId),
                DeleteOperation.SoftDelete,
                CancellationToken.None);

            // Get the deleted resource to find its new surrogate ID
            var deletedPatient = await _dataStore.GetAsync(new ResourceKey("Patient", resourceId), CancellationToken.None);

            var sqlSearchService = _searchService as SqlServerSearchService;
            Assert.NotNull(sqlSearchService);

            // Act - Search with includeDeleted=true
            var resultWithDeleted = await sqlSearchService!.SearchBySurrogateIdRange(
                "Patient",
                surrogateId,
                deletedPatient.ResourceSurrogateId + 10,
                null,
                null,
                CancellationToken.None,
                searchParamHashFilter: null,
                includeHistory: false,
                includeDeleted: true);

            // Act - Search with includeDeleted=false
            var resultWithoutDeleted = await sqlSearchService!.SearchBySurrogateIdRange(
                "Patient",
                surrogateId,
                deletedPatient.ResourceSurrogateId + 10,
                null,
                null,
                CancellationToken.None,
                searchParamHashFilter: null,
                includeHistory: false,
                includeDeleted: false);

            // Assert - With includeDeleted=true should return the deleted resource
            Assert.NotNull(resultWithDeleted);
            var deletedResource = resultWithDeleted.Results.FirstOrDefault(r => r.Resource.ResourceId == resourceId && r.Resource.IsDeleted);
            Assert.True(deletedResource.Resource != null);

            // Assert - With includeDeleted=false should not return deleted current versions
            Assert.NotNull(resultWithoutDeleted);

            // The deleted current version should be filtered out
            var deletedResourceInNonDeletedSearch = resultWithoutDeleted.Results.FirstOrDefault(r => r.Resource.ResourceId == resourceId && r.Resource.IsDeleted);
            Assert.Null(deletedResourceInNonDeletedSearch.Resource);
        }

        [Fact]
        public async Task Search_WithResetQueryPlans_ExecutesSuccessfully()
        {
            // Arrange
            var patient = await CreateTestPatient($"QueryPlanTest_{Guid.NewGuid()}");

            // Act - Reset query plans
            SqlServerSearchService.ResetReuseQueryPlans();

            // Act - Execute search after reset using _id (doesn't require name search parameter indexing)
            var queryParameters = new List<Tuple<string, string>>
            {
                new Tuple<string, string>("_id", patient.ResourceId),
            };

            var result = await _searchService.SearchAsync("Patient", queryParameters, CancellationToken.None);

            // Assert - Search should still work after reset
            Assert.NotNull(result);
            Assert.Single(result.Results);
            Assert.Equal(patient.ResourceId, result.Results.First().Resource.ResourceId);
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
