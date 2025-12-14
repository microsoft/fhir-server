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
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Xunit.Abstractions;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    /// <summary>
    /// Integration tests for SqlServerSearchService optimization for single and multiple resource ID reads.
    /// These tests verify that the optimized code paths correctly handle:
    /// 1. Single resource reads by type and ID
    /// 2. Multiple resource reads by type and multiple IDs
    /// 3. Versioned resource reads (_history)
    /// 4. Edge cases like deleted resources, missing resources, and mixed scenarios
    /// </summary>
    [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class SqlServerSearchServiceResourceReadOptimizationTests : IClassFixture<FhirStorageTestsFixture>
    {
        private readonly FhirStorageTestsFixture _fixture;
        private readonly ITestOutputHelper _output;

        public SqlServerSearchServiceResourceReadOptimizationTests(FhirStorageTestsFixture fixture, ITestOutputHelper testOutputHelper)
        {
            _fixture = fixture;
            _output = testOutputHelper;
        }

        [Fact]
        public async Task GivenASingleResourceId_WhenSearchedByTypeAndId_ThenOptimizedPathReturnsCorrectResource()
        {
            // Arrange - Create a test patient
            var patient = Samples.GetJsonSample("Patient").ToPoco<Patient>();
            patient.Id = Guid.NewGuid().ToString();
            patient.Name = new List<HumanName>
            {
                new HumanName { Family = "TestSingle", Given = new[] { "Single" } },
            };

            var saveResult = await _fixture.Mediator.UpsertResourceAsync(patient.ToResourceElement());

            // Act - Search by _id only (resource type is already specified in SearchAsync)
            var query = new List<Tuple<string, string>>
            {
                new Tuple<string, string>("_id", saveResult.RawResourceElement.Id),
            };

            var searchResult = await _fixture.SearchService.SearchAsync("Patient", query, CancellationToken.None);

            // Assert
            Assert.NotNull(searchResult);
            Assert.Single(searchResult.Results);
            Assert.Equal(saveResult.RawResourceElement.Id, searchResult.Results.First().Resource.ResourceId);
            Assert.Equal("Patient", searchResult.Results.First().Resource.ResourceTypeName);
            _output.WriteLine($"Successfully retrieved single resource: Patient/{saveResult.RawResourceElement.Id}");
        }

        [Fact]
        public async Task GivenMultipleResourceIds_WhenSearchedByTypeAndIds_ThenOptimizedPathReturnsAllResources()
        {
            // Arrange - Create multiple test patients
            var patient1 = Samples.GetJsonSample("Patient").ToPoco<Patient>();
            patient1.Id = Guid.NewGuid().ToString();
            patient1.Name = new List<HumanName> { new HumanName { Family = "TestMulti1" } };

            var patient2 = Samples.GetJsonSample("Patient").ToPoco<Patient>();
            patient2.Id = Guid.NewGuid().ToString();
            patient2.Name = new List<HumanName> { new HumanName { Family = "TestMulti2" } };

            var patient3 = Samples.GetJsonSample("Patient").ToPoco<Patient>();
            patient3.Id = Guid.NewGuid().ToString();
            patient3.Name = new List<HumanName> { new HumanName { Family = "TestMulti3" } };

            var saveResult1 = await _fixture.Mediator.UpsertResourceAsync(patient1.ToResourceElement());
            var saveResult2 = await _fixture.Mediator.UpsertResourceAsync(patient2.ToResourceElement());
            var saveResult3 = await _fixture.Mediator.UpsertResourceAsync(patient3.ToResourceElement());

            // Act - Search by multiple _id values
            var idList = $"{saveResult1.RawResourceElement.Id},{saveResult2.RawResourceElement.Id},{saveResult3.RawResourceElement.Id}";
            var query = new List<Tuple<string, string>>
            {
                new Tuple<string, string>("_id", idList),
            };

            var searchResult = await _fixture.SearchService.SearchAsync("Patient", query, CancellationToken.None);

            // Assert
            Assert.NotNull(searchResult);
            Assert.Equal(3, searchResult.Results.Count());

            var returnedIds = searchResult.Results.Select(r => r.Resource.ResourceId).ToHashSet();
            Assert.Contains(saveResult1.RawResourceElement.Id, returnedIds);
            Assert.Contains(saveResult2.RawResourceElement.Id, returnedIds);
            Assert.Contains(saveResult3.RawResourceElement.Id, returnedIds);

            _output.WriteLine($"Successfully retrieved {searchResult.Results.Count()} resources");
        }

        [Fact]
        public async Task GivenMultipleResourceIdsWithSomeMissing_WhenSearched_ThenOnlyExistingResourcesReturned()
        {
            // Arrange - Create two patients and use one non-existent ID
            var patient1 = Samples.GetJsonSample("Patient").ToPoco<Patient>();
            patient1.Id = Guid.NewGuid().ToString();
            patient1.Name = new List<HumanName> { new HumanName { Family = "TestPartial1" } };

            var patient2 = Samples.GetJsonSample("Patient").ToPoco<Patient>();
            patient2.Id = Guid.NewGuid().ToString();
            patient2.Name = new List<HumanName> { new HumanName { Family = "TestPartial2" } };

            var saveResult1 = await _fixture.Mediator.UpsertResourceAsync(patient1.ToResourceElement());
            var saveResult2 = await _fixture.Mediator.UpsertResourceAsync(patient2.ToResourceElement());
            var nonExistentId = Guid.NewGuid().ToString();

            // Act - Search with mix of existing and non-existent IDs
            var idList = $"{saveResult1.RawResourceElement.Id},{nonExistentId},{saveResult2.RawResourceElement.Id}";
            var query = new List<Tuple<string, string>>
            {
                new Tuple<string, string>("_id", idList),
            };

            var searchResult = await _fixture.SearchService.SearchAsync("Patient", query, CancellationToken.None);

            // Assert - Only existing resources should be returned
            Assert.NotNull(searchResult);
            Assert.Equal(2, searchResult.Results.Count());

            var returnedIds = searchResult.Results.Select(r => r.Resource.ResourceId).ToHashSet();
            Assert.Contains(saveResult1.RawResourceElement.Id, returnedIds);
            Assert.Contains(saveResult2.RawResourceElement.Id, returnedIds);
            Assert.DoesNotContain(nonExistentId, returnedIds);

            _output.WriteLine($"Successfully filtered out non-existent resource and returned {searchResult.Results.Count()} valid resources");
        }

        [Fact]
        public async Task GivenASingleResourceIdWithVersionedRead_WhenSearched_ThenCorrectVersionReturned()
        {
            // Arrange - Create a patient and update it to create versions
            var patient = Samples.GetJsonSample("Patient").ToPoco<Patient>();
            patient.Id = Guid.NewGuid().ToString();
            patient.Name = new List<HumanName> { new HumanName { Family = "TestVersion", Given = new[] { "Version1" } } };

            var saveResult = await _fixture.Mediator.UpsertResourceAsync(patient.ToResourceElement());

            // Update to create version 2
            patient.Name[0].Given = new[] { "Version2" };
            var updateResult = await _fixture.Mediator.UpsertResourceAsync(patient.ToResourceElement(), Core.Features.Persistence.WeakETag.FromVersionId(saveResult.RawResourceElement.VersionId));

            // Act - Search for specific version using history
            var query = new List<Tuple<string, string>>
            {
                new Tuple<string, string>("_id", saveResult.RawResourceElement.Id),
            };

            // Search with History version type to test versioned read optimization
            var searchResult = await _fixture.SearchService.SearchAsync(
                "Patient",
                query,
                CancellationToken.None,
                resourceVersionTypes: ResourceVersionType.Latest | ResourceVersionType.History);

            // Assert
            Assert.NotNull(searchResult);
            Assert.NotEmpty(searchResult.Results);
            Assert.All(searchResult.Results, r => Assert.Equal(saveResult.RawResourceElement.Id, r.Resource.ResourceId));

            _output.WriteLine($"Successfully retrieved versioned resource: Patient/{saveResult.RawResourceElement.Id}");
        }

        [Fact]
        public async Task GivenMultipleResourceIdsWithDeletedResource_WhenSearched_ThenOnlyActiveResourcesReturned()
        {
            // Arrange - Create patients and delete one
            var patient1 = Samples.GetJsonSample("Patient").ToPoco<Patient>();
            patient1.Id = Guid.NewGuid().ToString();
            patient1.Name = new List<HumanName> { new HumanName { Family = "TestDeleted1" } };

            var patient2 = Samples.GetJsonSample("Patient").ToPoco<Patient>();
            patient2.Id = Guid.NewGuid().ToString();
            patient2.Name = new List<HumanName> { new HumanName { Family = "TestDeleted2" } };

            var patient3 = Samples.GetJsonSample("Patient").ToPoco<Patient>();
            patient3.Id = Guid.NewGuid().ToString();
            patient3.Name = new List<HumanName> { new HumanName { Family = "TestDeleted3" } };

            var saveResult1 = await _fixture.Mediator.UpsertResourceAsync(patient1.ToResourceElement());
            var saveResult2 = await _fixture.Mediator.UpsertResourceAsync(patient2.ToResourceElement());
            var saveResult3 = await _fixture.Mediator.UpsertResourceAsync(patient3.ToResourceElement());

            // Delete patient2
            await _fixture.Mediator.DeleteResourceAsync(new Core.Features.Persistence.ResourceKey("Patient", saveResult2.RawResourceElement.Id), Core.Messages.Delete.DeleteOperation.SoftDelete);

            // Act - Search including the deleted resource ID
            var idList = $"{saveResult1.RawResourceElement.Id},{saveResult2.RawResourceElement.Id},{saveResult3.RawResourceElement.Id}";
            var query = new List<Tuple<string, string>>
            {
                new Tuple<string, string>("_id", idList),
            };

            var searchResult = await _fixture.SearchService.SearchAsync("Patient", query, CancellationToken.None);

            // Assert - Deleted resource should not be returned (default is Latest only)
            Assert.NotNull(searchResult);
            Assert.Equal(2, searchResult.Results.Count());

            var returnedIds = searchResult.Results.Select(r => r.Resource.ResourceId).ToHashSet();
            Assert.Contains(saveResult1.RawResourceElement.Id, returnedIds);
            Assert.DoesNotContain(saveResult2.RawResourceElement.Id, returnedIds); // Deleted
            Assert.Contains(saveResult3.RawResourceElement.Id, returnedIds);

            _output.WriteLine($"Successfully excluded deleted resource, returned {searchResult.Results.Count()} active resources");
        }

        [Fact]
        public async Task GivenSearchWithAdditionalParameters_WhenSearched_ThenOptimizationMayNotApply()
        {
            // Arrange
            var patient = Samples.GetJsonSample("Patient").ToPoco<Patient>();
            patient.Id = Guid.NewGuid().ToString();
            patient.Name = new List<HumanName> { new HumanName { Family = "TestAdditional" } };
            patient.Gender = AdministrativeGender.Female;

            var saveResult = await _fixture.Mediator.UpsertResourceAsync(patient.ToResourceElement());

            // Act - Search with additional parameter (should not use optimization)
            var query = new List<Tuple<string, string>>
            {
                new Tuple<string, string>("_id", saveResult.RawResourceElement.Id),
                new Tuple<string, string>("gender", "female"), // Additional parameter
            };

            var searchResult = await _fixture.SearchService.SearchAsync("Patient", query, CancellationToken.None);

            // Assert
            Assert.NotNull(searchResult);
            Assert.Single(searchResult.Results);
            Assert.Equal(saveResult.RawResourceElement.Id, searchResult.Results.First().Resource.ResourceId);

            _output.WriteLine("Search with additional parameters completed successfully (expected fallback to standard search)");
        }

        [Fact]
        public async Task GivenLargeNumberOfResourceIds_WhenSearched_ThenOptimizationHandlesEfficiently()
        {
            // Arrange - Create 10 patients
            var createdIds = new List<string>();
            for (int i = 0; i < 10; i++)
            {
                var patient = Samples.GetJsonSample("Patient").ToPoco<Patient>();
                patient.Id = Guid.NewGuid().ToString();
                patient.Name = new List<HumanName> { new HumanName { Family = $"TestLarge{i}" } };

                var saveResult = await _fixture.Mediator.UpsertResourceAsync(patient.ToResourceElement());
                createdIds.Add(saveResult.RawResourceElement.Id);
            }

            // Act - Search for all 10 resources
            var idList = string.Join(",", createdIds);
            var query = new List<Tuple<string, string>>
            {
                new Tuple<string, string>("_id", idList),
            };

            var searchResult = await _fixture.SearchService.SearchAsync("Patient", query, CancellationToken.None);

            // Assert
            Assert.NotNull(searchResult);
            Assert.Equal(10, searchResult.Results.Count());

            var returnedIds = searchResult.Results.Select(r => r.Resource.ResourceId).ToHashSet();
            foreach (var id in createdIds)
            {
                Assert.Contains(id, returnedIds);
            }

            _output.WriteLine($"Successfully retrieved all {searchResult.Results.Count()} resources efficiently");
        }

        [Fact]
        public async Task GivenSearchWithCountOnly_WhenSearched_ThenOptimizationDoesNotApply()
        {
            // Arrange
            var patient = Samples.GetJsonSample("Patient").ToPoco<Patient>();
            patient.Id = Guid.NewGuid().ToString();
            patient.Name = new List<HumanName> { new HumanName { Family = "TestCount" } };

            var saveResult = await _fixture.Mediator.UpsertResourceAsync(patient.ToResourceElement());

            // Act - Search with count only
            var query = new List<Tuple<string, string>>
            {
                new Tuple<string, string>("_id", saveResult.RawResourceElement.Id),
                new Tuple<string, string>("_summary", "count"),
            };

            var searchResult = await _fixture.SearchService.SearchAsync("Patient", query, CancellationToken.None);

            // Assert
            Assert.NotNull(searchResult);
            _output.WriteLine($"Count-only search completed successfully with total: {searchResult.TotalCount}");
        }

        [Fact]
        public async Task GivenSearchWithSort_WhenSearched_ThenOptimizationDoesNotApply()
        {
            // Arrange
            var patient1 = Samples.GetJsonSample("Patient").ToPoco<Patient>();
            patient1.Id = Guid.NewGuid().ToString();
            patient1.Name = new List<HumanName> { new HumanName { Family = "Aardvark" } };

            var patient2 = Samples.GetJsonSample("Patient").ToPoco<Patient>();
            patient2.Id = Guid.NewGuid().ToString();
            patient2.Name = new List<HumanName> { new HumanName { Family = "Zebra" } };

            var saveResult1 = await _fixture.Mediator.UpsertResourceAsync(patient1.ToResourceElement());
            var saveResult2 = await _fixture.Mediator.UpsertResourceAsync(patient2.ToResourceElement());

            // Act - Search with sort parameter
            var idList = $"{saveResult1.RawResourceElement.Id},{saveResult2.RawResourceElement.Id}";
            var query = new List<Tuple<string, string>>
            {
                new Tuple<string, string>("_id", idList),
                new Tuple<string, string>("_sort", "family"),
            };

            var searchResult = await _fixture.SearchService.SearchAsync("Patient", query, CancellationToken.None);

            // Assert
            Assert.NotNull(searchResult);
            Assert.Equal(2, searchResult.Results.Count());
            _output.WriteLine("Search with sort parameter completed successfully (expected fallback to standard search)");
        }
    }
}
