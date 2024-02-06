// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Reindex
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.IndexAndReindex)]
    public class ReindexUtilitiesTests
    {
        private readonly IFhirDataStore _fhirDataStore = Substitute.For<IFhirDataStore>();
        private readonly ISearchIndexer _searchIndexer = Substitute.For<ISearchIndexer>();
        private readonly ISearchParameterStatusDataStore _searchParameterStatusDataStore = Substitute.For<ISearchParameterStatusDataStore>();
        private readonly ResourceDeserializer _resourceDeserializer = Deserializers.ResourceDeserializer;
        private readonly ISupportedSearchParameterDefinitionManager _searchParameterDefinitionManager = Substitute.For<ISupportedSearchParameterDefinitionManager>();
        private readonly SearchParameterStatusManager _searchParameterStatusManager;
        private readonly ISearchParameterSupportResolver _searchParameterSupportResolver = Substitute.For<ISearchParameterSupportResolver>();
        private readonly IMediator _mediator = Substitute.For<IMediator>();
        private readonly IResourceWrapperFactory _resourceWrapperFactory = Substitute.For<IResourceWrapperFactory>();

        private readonly ITestOutputHelper _output;
        private IReadOnlyDictionary<string, string> _searchParameterHashMap;
        private readonly ReindexUtilities _reindexUtilities;

        public ReindexUtilitiesTests(ITestOutputHelper output)
        {
            _output = output;
            _searchParameterHashMap = new Dictionary<string, string>() { { "Patient", "hash1" } };
            Func<Health.Extensions.DependencyInjection.IScoped<IFhirDataStore>> fhirDataStoreScope = () => _fhirDataStore.CreateMockScope();
            _searchParameterStatusManager = new SearchParameterStatusManager(_searchParameterStatusDataStore, _searchParameterDefinitionManager, _searchParameterSupportResolver, _mediator, NullLogger<SearchParameterStatusManager>.Instance);
            _reindexUtilities = new ReindexUtilities(fhirDataStoreScope, _searchParameterStatusManager, _resourceWrapperFactory);
        }

        [Fact]
        public async Task GivenResourcesWithUnchangedOrChangedIndices_WhenResultsProcessed_ThenCorrectResourcesHaveIndicesUpdated()
        {
            var searchIndexEntry1 = new SearchIndexEntry(new Core.Models.SearchParameterInfo("param1", "param1"), new StringSearchValue("value1"));
            var searchIndexEntry2 = new SearchIndexEntry(new Core.Models.SearchParameterInfo("param2", "param2"), new StringSearchValue("value2"));

            var searchIndices1 = new List<SearchIndexEntry>() { searchIndexEntry1 };
            var searchIndices2 = new List<SearchIndexEntry>() { searchIndexEntry2 };

            _searchIndexer.Extract(Arg.Any<Core.Models.ResourceElement>()).Returns(searchIndices1);

            var entry1 = CreateSearchResultEntry("Patient", searchIndices1);
            _output.WriteLine($"Loaded Patient with id: {entry1.Resource.ResourceId}");
            var entry2 = CreateSearchResultEntry("Observation-For-Patient-f001", searchIndices2);
            _output.WriteLine($"Loaded Observation with id: {entry2.Resource.ResourceId}");
            var resultList = new List<SearchResultEntry>();
            resultList.Add(entry1);
            resultList.Add(entry2);
            var result = new SearchResult(resultList, "token", null, new List<Tuple<string, string>>());

            await _reindexUtilities.ProcessSearchResultsAsync(result, _searchParameterHashMap, CancellationToken.None);

            await _fhirDataStore.Received().BulkUpdateSearchParameterIndicesAsync(
                Arg.Is<IReadOnlyCollection<ResourceWrapper>>(c => c.Count() == 2), Arg.Any<CancellationToken>());
        }

        private SearchResultEntry CreateSearchResultEntry(string jsonName, IReadOnlyCollection<SearchIndexEntry> searchIndices)
        {
            var json = Samples.GetJson(jsonName);
            var rawResource = new RawResource(json, FhirResourceFormat.Json, isMetaSet: false);
            var resourceRequest = Substitute.For<ResourceRequest>();
            var compartmentIndices = Substitute.For<CompartmentIndices>();
            var resourceElement = _resourceDeserializer.DeserializeRaw(rawResource, "v1", DateTimeOffset.UtcNow);
            var wrapper = new ResourceWrapper(resourceElement, rawResource, resourceRequest, false, searchIndices, compartmentIndices, new List<KeyValuePair<string, string>>(), "hash");
            var entry = new SearchResultEntry(wrapper);

            return entry;
        }
    }
}
