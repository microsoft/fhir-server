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
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    public class SearchServiceTests
    {
        private const string ParamNameSearchOptionsFactory = "searchOptionsFactory";
        private static readonly Uri SearchUrl = new Uri("http://test");

        private readonly IUrlResolver _urlResolver = Substitute.For<IUrlResolver>();
        private readonly ISearchOptionsFactory _searchOptionsFactory = Substitute.For<ISearchOptionsFactory>();
        private readonly IBundleFactory _bundleFactory;
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor = Substitute.For<IFhirRequestContextAccessor>();

        private readonly TestSearchService _searchService;
        private readonly RawResourceFactory _rawResourceFactory;
        private readonly ResourceRequest _resourceRequest = new ResourceRequest("http://fhir", HttpMethod.Post);
        private readonly string _correlationId;
        private readonly IDataStore _dataStore;

        public SearchServiceTests()
        {
            _bundleFactory = new BundleFactory(_urlResolver, _fhirRequestContextAccessor);
            _dataStore = Substitute.For<IDataStore>();

            _searchOptionsFactory.Create(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>())
                .Returns(x => new SearchOptions());

            _searchService = new TestSearchService(_searchOptionsFactory, _bundleFactory, _dataStore);
            _rawResourceFactory = new RawResourceFactory(new FhirJsonSerializer());

            _urlResolver.ResolveSearchUrl(Arg.Any<string>(), Arg.Any<IEnumerable<Tuple<string, string>>>()).Returns(SearchUrl);

            _correlationId = Guid.NewGuid().ToString();
            _fhirRequestContextAccessor.FhirRequestContext.CorrelationId.Returns(_correlationId);
        }

        [Fact]
        public void GivenANullSearchOptionsFactory_WhenInitializing_ThenInitializationShouldFail()
        {
            Assert.Throws<ArgumentNullException>(ParamNameSearchOptionsFactory, () => new TestSearchService(null, _bundleFactory, null));
        }

        [Fact]
        public async Task GivenAnySearching_WhenSearched_ThenSelfLinkShouldBeReturned()
        {
            const string resourceType = "Patient";

            _searchService.SearchImplementation = options => new SearchResult(new ResourceWrapper[0], null);

            _urlResolver.ResolveSearchUrl(resourceType: null, unsupportedSearchParams: null, continuationToken: null).Returns(SearchUrl);

            Bundle actual = await _searchService.SearchAsync(resourceType, null);

            Assert.NotNull(actual);
            Assert.Equal(SearchUrl, actual.SelfLink);
            Assert.Equal(_correlationId.ToString(), actual.Id);
        }

        [Fact]
        public async Task GivenNoMatch_WhenSearching_ThenSearchReturnsEmptyBundle()
        {
            const string resourceType = "Patient";

            _searchService.SearchImplementation = options => new SearchResult(new ResourceWrapper[0], null);

            Bundle actual = await _searchService.SearchAsync(resourceType, null);

            Assert.NotNull(actual);
            Assert.Equal(Bundle.BundleType.Searchset, actual.Type);
            Assert.Equal(_correlationId.ToString(), actual.Id);
        }

        [Fact]
        public async Task GivenMatches_WhenSearching_ThenSearchReturnsBundleWithResults()
        {
            const string resourceType = "Observation";

            Observation observation1 = new Observation() { Id = "123" };
            Observation observation2 = new Observation() { Id = "abc" };

            ResourceWrapper[] resourceWrappers = new ResourceWrapper[]
            {
                new ResourceWrapper(observation1, _rawResourceFactory.Create(observation1), _resourceRequest, false, null, null, null),
                new ResourceWrapper(observation2, _rawResourceFactory.Create(observation2), _resourceRequest, false, null, null, null),
            };

            _searchService.SearchImplementation = options => new SearchResult(resourceWrappers, null);

            _urlResolver.ResolveResourceUrl(Arg.Any<Resource>()).Returns(x => new Uri($"{SearchUrl}/{x.ArgAt<Resource>(0).Id}"));

            Bundle actual = await _searchService.SearchAsync(resourceType, null);

            Assert.NotNull(actual);
            Assert.Collection(
                actual.Entry,
                e => ValidateEntry(observation1, e),
                e => ValidateEntry(observation2, e));
            Assert.Equal(Bundle.BundleType.Searchset, actual.Type);
            Assert.Equal(_correlationId.ToString(), actual.Id);

            void ValidateEntry(Observation expected, Bundle.EntryComponent actualEntry)
            {
                Assert.NotNull(actualEntry);
                Assert.NotNull(actualEntry.Resource);
                Assert.Equal(expected.Id, actualEntry.Resource.Id);
                Assert.Equal($"{SearchUrl}/{expected.Id}", actualEntry.FullUrl);
                Assert.NotNull(actualEntry.Search);
                Assert.Equal(Bundle.SearchEntryMode.Match, actualEntry.Search.Mode);
            }
        }

        [Fact]
        public async Task GivenMoreMatchesAvailable_WhenSearching_ThenSearchReturnsBundleWithNextLink()
        {
            const string resourceType = "Observation";
            const string searchToken = "abc";
            Uri continuationLink = new Uri($"http://{searchToken}");

            _searchService.SearchImplementation = options => new SearchResult(new ResourceWrapper[0], searchToken);

            _urlResolver.ResolveSearchUrl(resourceType, unsupportedSearchParams: null, continuationToken: searchToken).Returns(continuationLink);

            Bundle actual = await _searchService.SearchAsync(resourceType, null);

            Assert.NotNull(actual);
            Assert.Equal(continuationLink, actual.NextLink);
            Assert.Equal(_correlationId, actual.Id);
        }

        [Fact]
        public async Task GivenAMissingResourceId_WhenSearchingHistory_ThenAResourceNotFoundExceptionIsThrown()
        {
            const string resourceType = "Observation";
            const string resourceId = "abc";

            _searchService.SearchImplementation = options => new SearchResult(new ResourceWrapper[0], null);

            await Assert.ThrowsAsync<ResourceNotFoundException>(() => _searchService.SearchHistoryAsync(resourceType, resourceId, null, null, null, null, CancellationToken.None));
        }

        [Fact]
        public async Task GivenResourceId_WhenSearchingHistoryWithSinceButNoResults_ThenBundleIsReturned()
        {
            const string resourceType = "Observation";
            const string resourceId = "abc";

            var observation = new Observation { Id = resourceId };

            var resourceWrapper =
                new ResourceWrapper(observation, _rawResourceFactory.Create(observation), _resourceRequest, false, null, null, null);
            _searchService.SearchImplementation = options => new SearchResult(new ResourceWrapper[0], null);
            _urlResolver.ResolveRouteUrl(Arg.Any<string>(), Arg.Any<IEnumerable<Tuple<string, string>>>()).Returns(new Uri("http://narwhal"));

            _dataStore.GetAsync(Arg.Any<ResourceKey>(), Arg.Any<CancellationToken>()).Returns(resourceWrapper);

            var bundle = await _searchService.SearchHistoryAsync(resourceType, resourceId, PartialDateTime.Parse("2018"), null, null, null, CancellationToken.None);

            Assert.Empty(bundle.Entry);
        }

        private class TestSearchService : SearchService
        {
            public TestSearchService(ISearchOptionsFactory searchOptionsFactory, IBundleFactory bundleFactory, IDataStore dataStore)
                : base(searchOptionsFactory, bundleFactory, dataStore)
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
