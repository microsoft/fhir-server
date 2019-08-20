// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    public class SearchServiceTests
    {
        private static readonly Uri SearchUrl = new Uri("http://test");

        private readonly ISearchOptionsFactory _searchOptionsFactory = Substitute.For<ISearchOptionsFactory>();
        private readonly IFhirDataStore _fhirDataStore = Substitute.For<IFhirDataStore>();

        private readonly TestSearchService _searchService;
        private readonly RawResourceFactory _rawResourceFactory;
        private readonly ResourceRequest _resourceRequest = new ResourceRequest(HttpMethod.Post, "http://fhir");

        private readonly IReadOnlyList<Tuple<string, string>> _queryParameters = new Tuple<string, string>[0];
        private readonly IReadOnlyList<Tuple<string, string>> _unsupportedQueryParameters = new Tuple<string, string>[0];
        private readonly IReadOnlyList<(string searchParameterName, string reason)> _unsupportedSortingParameters = Array.Empty<(string searchParameterName, string reason)>();

        public SearchServiceTests()
        {
            _searchOptionsFactory.Create(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>())
                .Returns(x => new SearchOptions());

            _searchService = new TestSearchService(_searchOptionsFactory, _fhirDataStore, ModelInfoProvider.Instance);
            _rawResourceFactory = new RawResourceFactory(new FhirJsonSerializer());
        }

        [Fact]
        public async Task GivenSearching_WhenSearched_ThenCorrectOptionIsUsedAndCorrectSearchResultsReturned()
        {
            const string resourceType = "Patient";

            var expectedSearchOptions = new SearchOptions();

            _searchOptionsFactory.Create(resourceType, _queryParameters).Returns(expectedSearchOptions);

            var expectedSearchResult = new SearchResult(new ResourceWrapper[0], _unsupportedQueryParameters, _unsupportedSortingParameters, null);

            _searchService.SearchImplementation = options =>
            {
                Assert.Same(expectedSearchOptions, options);

                return expectedSearchResult;
            };

            SearchResult actual = await _searchService.SearchAsync(resourceType, _queryParameters, CancellationToken.None);

            Assert.Same(expectedSearchResult, actual);
        }

        [Fact]
        public async Task GivenCompartmentSearching_WhenSearched_ThenCorrectOptionIsUsedAndCorrectSearchResultsReturned()
        {
            const string compartmentType = "Patient";
            const string compartmentId = "123";
            const string resourceType = "Observation";

            var expectedSearchOptions = new SearchOptions();

            _searchOptionsFactory.Create(compartmentType, compartmentId, resourceType, _queryParameters).Returns(expectedSearchOptions);

            var expectedSearchResult = new SearchResult(new ResourceWrapper[0], _unsupportedQueryParameters, _unsupportedSortingParameters, null);

            _searchService.SearchImplementation = options =>
            {
                Assert.Same(expectedSearchOptions, options);

                return expectedSearchResult;
            };

            SearchResult actual = await _searchService.SearchCompartmentAsync(compartmentType, compartmentId, resourceType, _queryParameters, CancellationToken.None);

            Assert.Same(expectedSearchResult, actual);
        }

        [Fact]
        public async Task GivenAMissingResourceId_WhenSearchingHistory_ThenAResourceNotFoundExceptionIsThrown()
        {
            const string resourceType = "Observation";
            const string resourceId = "abc";

            _searchService.SearchImplementation = options => new SearchResult(new ResourceWrapper[0], _unsupportedQueryParameters, _unsupportedSortingParameters, null);

            await Assert.ThrowsAsync<ResourceNotFoundException>(() => _searchService.SearchHistoryAsync(resourceType, resourceId, null, null, null, null, null, CancellationToken.None));
        }

        [Fact]
        public async Task GivenResourceId_WhenSearchingHistoryWithSinceButNoResults_ThenBundleIsReturned()
        {
            const string resourceType = "Observation";
            const string resourceId = "abc";

            var observation = new Observation { Id = resourceId }.ToResourceElement();

            var resourceWrapper =
                new ResourceWrapper(observation, _rawResourceFactory.Create(observation), _resourceRequest, false, null, null, null);
            _searchService.SearchImplementation = options => new SearchResult(new ResourceWrapper[0], _unsupportedQueryParameters, _unsupportedSortingParameters, null);

            _fhirDataStore.GetAsync(Arg.Any<ResourceKey>(), Arg.Any<CancellationToken>()).Returns(resourceWrapper);

            SearchResult searchResult = await _searchService.SearchHistoryAsync(resourceType, resourceId, PartialDateTime.Parse("2018"), null, null, null, null, CancellationToken.None);

            Assert.Empty(searchResult.Results);
        }

        private class TestSearchService : SearchService
        {
            public TestSearchService(ISearchOptionsFactory searchOptionsFactory, IFhirDataStore fhirDataStore, IModelInfoProvider modelInfoProvider)
                : base(searchOptionsFactory, fhirDataStore, modelInfoProvider)
            {
                SearchImplementation = options => null;
            }

            public Func<SearchOptions, SearchResult> SearchImplementation { get; set; }

            protected override Task<SearchResult> SearchInternalAsync(
                SearchOptions searchOptions,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(SearchImplementation(searchOptions));
            }

            protected override Task<SearchResult> SearchHistoryInternalAsync(
                SearchOptions searchOptions,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(SearchImplementation(searchOptions));
            }
        }
    }
}
