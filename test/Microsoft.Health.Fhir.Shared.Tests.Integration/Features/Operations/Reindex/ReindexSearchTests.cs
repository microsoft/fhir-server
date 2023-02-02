// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.Integration.Features.Operations.Reindex
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.IndexAndReindex)]
    [FhirStorageTestsFixtureArgumentSets(DataStore.All)]

    public class ReindexSearchTests : IClassFixture<FhirStorageTestsFixture>
    {
        private readonly IScoped<IFhirDataStore> _scopedDataStore;
        private readonly IScoped<ISearchService> _searchService;
        private readonly SearchParameterDefinitionManager _searchParameterDefinitionManager;

        private readonly ISearchIndexer _searchIndexer = Substitute.For<ISearchIndexer>();

        public ReindexSearchTests(FhirStorageTestsFixture fixture)
        {
            _scopedDataStore = fixture.DataStore.CreateMockScope();
            _searchService = fixture.SearchService.CreateMockScope();
            _searchParameterDefinitionManager = fixture.SearchParameterDefinitionManager;
        }

        [Fact]
        public async Task GivenResourceWithMatchingHash_WhenPerformingReindexSearch_ThenResourceShouldNotBeReturned()
        {
            ResourceWrapper testPatient = null;

            try
            {
                UpsertOutcome outcome = await UpsertPatientData();
                testPatient = outcome.Wrapper;

                var patientHash = testPatient.SearchParameterHash;

                var queryParametersList = new List<Tuple<string, string>>()
                {
                    Tuple.Create(KnownQueryParameterNames.Count, "100"),
                    Tuple.Create(KnownQueryParameterNames.Type, "Patient"),
                };

                // Pass in the same hash value
                SearchResult searchResult = await _searchService.Value.SearchForReindexAsync(queryParametersList, patientHash, false, CancellationToken.None);

                // A reindex search should return all the resources that have a different hash value than the one specified
                Assert.Empty(searchResult.Results);
            }
            finally
            {
                if (testPatient != null)
                {
                    await _scopedDataStore.Value.HardDeleteAsync(testPatient.ToResourceKey(), false, CancellationToken.None);
                }
            }
        }

        [Fact]
        public async Task GivenResourceWithDifferentHash_WhenPerformingReindexSearch_ThenResourceShouldBeReturned()
        {
            ResourceWrapper testPatient = null;

            try
            {
                UpsertOutcome outcome = await UpsertPatientData();
                testPatient = outcome.Wrapper;

                var queryParametersList = new List<Tuple<string, string>>()
                {
                    Tuple.Create(KnownQueryParameterNames.Count, "100"),
                    Tuple.Create(KnownQueryParameterNames.Type, "Patient"),
                };

                // Pass in a different hash value
                SearchResult searchResult = await _searchService.Value.SearchForReindexAsync(queryParametersList, "differentHash", false, CancellationToken.None);

                // A reindex search should return all the resources that have a different hash value than the one specified.
                Assert.Single(searchResult.Results);
            }
            finally
            {
                if (testPatient != null)
                {
                    await _scopedDataStore.Value.HardDeleteAsync(testPatient.ToResourceKey(), false, CancellationToken.None);
                }
            }
        }

        private async Task<UpsertOutcome> UpsertPatientData()
        {
            var json = Samples.GetJson("Patient");
            var rawResource = new RawResource(json, FhirResourceFormat.Json, isMetaSet: false);
            var resourceRequest = new ResourceRequest(WebRequestMethods.Http.Put);
            var compartmentIndices = Substitute.For<CompartmentIndices>();
            var resourceElement = Deserializers.ResourceDeserializer.DeserializeRaw(rawResource, "v1", DateTimeOffset.UtcNow);
            var searchIndices = new List<SearchIndexEntry>() { new SearchIndexEntry(new SearchParameterInfo("name", "name", ValueSets.SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name")) { SortStatus = SortParameterStatus.Enabled }, new StringSearchValue("alpha")) };

            var wrapper = new ResourceWrapper(
                resourceElement,
                rawResource,
                resourceRequest,
                false,
                searchIndices,
                compartmentIndices,
                new List<KeyValuePair<string, string>>(),
                _searchParameterDefinitionManager.GetSearchParameterHashForResourceType("Patient"));
            wrapper.SearchParameterHash = "hash";

            return await _scopedDataStore.Value.UpsertAsync(new ResourceWrapperOperation(wrapper, true, true, null, false), CancellationToken.None);
        }
    }
}
