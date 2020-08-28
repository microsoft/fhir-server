// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using NSubstitute;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Reindex
{
    public class ReindexUtilitiesTests
    {
        private readonly IFhirDataStore _fhirDataStore = Substitute.For<IFhirDataStore>();
        private readonly ISearchIndexer _searchIndexer = Substitute.For<ISearchIndexer>();
        private readonly ResourceDeserializer _resourceDeserializer = Deserializers.ResourceDeserializer;
        private readonly ITestOutputHelper _output;

        public ReindexUtilitiesTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async System.Threading.Tasks.Task GivenResourcesWithUnchangedOrChangedIndices_WhenResultsProcessed_ThenCorrectResourcesHaveIndicesUpdated()
        {
            Func<Health.Extensions.DependencyInjection.IScoped<IFhirDataStore>> fhirDataStoreScope = () => _fhirDataStore.CreateMockScope();
            var utilities = new ReindexUtilities(fhirDataStoreScope, _searchIndexer, _resourceDeserializer);

            var searchIndices1 = new List<SearchIndexEntry>() { new SearchIndexEntry(new Core.Models.SearchParameterInfo("param1"), new StringSearchValue("value1")) };
            var searchIndices2 = new List<SearchIndexEntry>() { new SearchIndexEntry(new Core.Models.SearchParameterInfo("param2"), new StringSearchValue("value2")) };

            _searchIndexer.Extract(Arg.Any<Core.Models.ResourceElement>()).Returns(searchIndices1);

            var entry1 = CreateSearchResultEntry("Patient", searchIndices1);
            _output.WriteLine($"Loaded Patient with id: {entry1.Resource.ResourceId}");
            var entry2 = CreateSearchResultEntry("Observation-For-Patient-f001", searchIndices2);
            _output.WriteLine($"Loaded Observation with id: {entry2.Resource.ResourceId}");
            var resultList = new List<SearchResultEntry>();
            resultList.Add(entry1);
            resultList.Add(entry2);
            var result = new SearchResult(resultList, new List<Tuple<string, string>>(), new List<(string, string)>(), "token");

            await utilities.ProcessSearchResultsAsync(result, "hash", CancellationToken.None);

            await _fhirDataStore.Received().UpdateSearchParameterHashBatchAsync(
                Arg.Is<IReadOnlyCollection<ResourceWrapper>>(
                    c => c.Where(r => r.SearchIndices == searchIndices1 && r.ResourceTypeName.Equals("Patient")).Count() == 1),
                Arg.Any<CancellationToken>());

            await _fhirDataStore.Received().UpdateSearchParameterIndicesBatchAsync(
                Arg.Is<IReadOnlyCollection<ResourceWrapper>>(
                    c => c.Where(r => r.SearchIndices == searchIndices1 && r.ResourceTypeName.Equals("Observation")).Count() == 1),
                Arg.Any<CancellationToken>());
        }

        private SearchResultEntry CreateSearchResultEntry(string jsonName, IReadOnlyCollection<SearchIndexEntry> searchIndices)
        {
            var json = Samples.GetJson(jsonName);
            var rawResource = new RawResource(json, FhirResourceFormat.Json);
            var resourceRequest = Substitute.For<ResourceRequest>();
            var compartmentIndices = Substitute.For<CompartmentIndices>();
            var resourceElement = _resourceDeserializer.DeserializeRaw(rawResource, "v1", DateTimeOffset.UtcNow);
            var wrapper = new ResourceWrapper(resourceElement, rawResource, resourceRequest, false, searchIndices, compartmentIndices, new List<KeyValuePair<string, string>>(), "hash");
            var entry = new SearchResultEntry(wrapper);

            return entry;
        }
    }
}
