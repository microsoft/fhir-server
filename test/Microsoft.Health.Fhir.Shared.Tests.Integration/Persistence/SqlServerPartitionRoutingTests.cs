// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Partitions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    /// <summary>
    /// Exercises partition routing with one application connection string and independent databases.
    /// </summary>
    [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class SqlServerPartitionRoutingTests : IAsyncLifetime
    {
        private readonly SqlServerFhirStorageTestsFixture _firstPartition;
        private readonly SqlServerFhirStorageTestsFixture _secondPartition;
        private readonly SqlServerFhirStorageTestsFixture _application;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqlServerPartitionRoutingTests"/> class.
        /// </summary>
        public SqlServerPartitionRoutingTests()
        {
            string firstDatabase = SqlServerFhirStorageTestsFixture.GetDatabaseName();
            string secondDatabase = SqlServerFhirStorageTestsFixture.GetDatabaseName();
            _firstPartition = new SqlServerFhirStorageTestsFixture(SchemaVersionConstants.Max, firstDatabase);
            _secondPartition = new SqlServerFhirStorageTestsFixture(SchemaVersionConstants.Max, secondDatabase);
            _application = new SqlServerFhirStorageTestsFixture(
                SchemaVersionConstants.Max,
                SqlServerFhirStorageTestsFixture.GetDatabaseName(),
                partitionConfiguration: new PartitionConfiguration
                {
                    Partitions =
                    {
                        new PartitionInfo { Id = "alpha", DatabaseName = firstDatabase },
                        new PartitionInfo { Id = "beta", DatabaseName = secondDatabase },
                    },
                });
        }

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            await _firstPartition.InitializeAsync();
            await _secondPartition.InitializeAsync();
            await _application.InitializeAsync();
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await Task.WhenAll(
                _application.DisposeAsync().AsTask(),
                _secondPartition.DisposeAsync().AsTask(),
                _firstPartition.DisposeAsync().AsTask());
        }

        /// <summary>
        /// Writes, reads, and updates the same resource ID without crossing partition databases.
        /// </summary>
        [Fact]
        public async Task GivenMultiplePartitions_WhenDataStoreWritesReadsAndUpdates_ThenOnlySelectedDatabaseIsUsed()
        {
            // Arrange
            string id = Guid.NewGuid().ToString();
            var store = _application.SqlServerFhirDataStore;
            var key = new ResourceKey("Patient", id);

            // Act
            SelectPartition("alpha");
            var firstCreate = await store.UpsertAsync(CreatePatient(_application, id, "Alpha"), CancellationToken.None);
            SelectPartition("beta");
            var secondCreate = await store.UpsertAsync(CreatePatient(_application, id, "Beta"), CancellationToken.None);

            // Assert
            Assert.Equal(SaveOutcomeType.Created, firstCreate.OutcomeType);
            Assert.Equal(SaveOutcomeType.Created, secondCreate.OutcomeType);
            AssertPatient(await store.GetAsync(key, CancellationToken.None), id, "Beta", "1");
            SelectPartition("alpha");
            AssertPatient(await store.GetAsync(key, CancellationToken.None), id, "Alpha", "1");

            var update = await store.UpsertAsync(CreatePatient(_application, id, "AlphaUpdated"), CancellationToken.None);
            Assert.Equal(SaveOutcomeType.Updated, update.OutcomeType);
            AssertPatient(await store.GetAsync(key, CancellationToken.None), id, "AlphaUpdated", "2");
            SelectPartition("beta");
            AssertPatient(await store.GetAsync(key, CancellationToken.None), id, "Beta", "1");

            // Verify physical destinations independently of the application's partition resolver.
            AssertPatient(await _firstPartition.SqlServerFhirDataStore.GetAsync(key, CancellationToken.None), id, "AlphaUpdated", "2");
            AssertPatient(await _secondPartition.SqlServerFhirDataStore.GetAsync(key, CancellationToken.None), id, "Beta", "1");
            SelectPartition(null);
            Assert.Null(await store.GetAsync(key, CancellationToken.None));
        }

        /// <summary>
        /// Searches independently seeded databases through generated SQL and the ID lookup shortcut.
        /// </summary>
        [Theory]
        [InlineData("family")]
        [InlineData("_id")]
        public async Task GivenMultiplePartitions_WhenSearchServiceSwitchesPartition_ThenOnlySelectedDatabaseIsSearched(string parameter)
        {
            // Arrange
            string id = Guid.NewGuid().ToString();
            await _firstPartition.SqlServerFhirDataStore.UpsertAsync(CreatePatient(_firstPartition, id, "Alpha"), CancellationToken.None);
            await _secondPartition.SqlServerFhirDataStore.UpsertAsync(CreatePatient(_secondPartition, id, "Beta"), CancellationToken.None);
            var searchService = _application.GetRequiredService<ISearchService>();
            var query = new[] { Tuple.Create(parameter, parameter == "_id" ? id : "Alpha,Beta") };

            // Act / Assert
            foreach (var (partition, family) in new[] { ("alpha", "Alpha"), ("beta", "Beta"), ("alpha", "Alpha") })
            {
                SelectPartition(partition);
                SearchResult result = await searchService.SearchAsync("Patient", query, CancellationToken.None);
                Assert.Empty(result.UnsupportedSearchParameters);
                AssertPatient(Assert.Single(result.Results).Resource, id, family, "1");
            }

            SelectPartition(null);
            SearchResult defaultResult = await searchService.SearchAsync("Patient", query, CancellationToken.None);
            Assert.Empty(defaultResult.Results);
        }

        private static ResourceWrapperOperation CreatePatient(SqlServerFhirStorageTestsFixture fixture, string id, string family)
        {
            var patient = new Patient
            {
                Id = id,
                Meta = new Meta { VersionId = "1", LastUpdated = DateTimeOffset.UtcNow },
                Name = { new HumanName { Family = family } },
            };
            var searchParameter = fixture.GetRequiredService<SearchParameterDefinitionManager>().GetSearchParameter("Patient", "family");
            var wrapper = new ResourceWrapper(
                patient.ToResourceElement(),
                new RawResource(new FhirJsonSerializer().SerializeToString(patient), FhirResourceFormat.Json, isMetaSet: true),
                new ResourceRequest("PUT"),
                deleted: false,
                new[] { new SearchIndexEntry(searchParameter, new StringSearchValue(family)) },
                compartmentIndices: null,
                lastModifiedClaims: null,
                searchParameterHash: "hash");
            return new ResourceWrapperOperation(wrapper, true, true, null, false, false, bundleResourceContext: null);
        }

        private static void AssertPatient(ResourceWrapper wrapper, string id, string family, string version)
        {
            Assert.NotNull(wrapper);
            Assert.Equal(id, wrapper.ResourceId);
            Assert.Equal(version, wrapper.Version);
            var patient = new FhirJsonParser().Parse<Patient>(wrapper.RawResource.Data);
            Assert.Equal(family, Assert.Single(patient.Name).Family);
        }

        private void SelectPartition(string partition)
        {
            _application.GetRequiredService<RequestContextAccessor<IFhirRequestContext>>().RequestContext = new FhirRequestContext(
                "GET",
                "https://localhost/Patient",
                "https://localhost/",
                Guid.NewGuid().ToString(),
                new Dictionary<string, StringValues>(),
                new Dictionary<string, StringValues>())
            {
                PartitionId = partition,
                Principal = new ClaimsPrincipal(new ClaimsIdentity()),
            };
        }
    }
}
