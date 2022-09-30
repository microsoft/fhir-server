// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Behavior;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class ListSearchBehaviorTests
    {
        private readonly FhirJsonParser _fhirJsonParser = new FhirJsonParser();

        private readonly IFhirDataStore _fhirDataStore;

        private IScoped<IFhirDataStore> _scopedDataStore;

        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        private readonly CancellationToken _cancellationToken;

        private readonly ISearchOptionsFactory _searchOptionsFactory;

        private readonly IBundleFactory _bundleFactory;

        private ResourceElement _nonEmptyBundle;

        public ListSearchBehaviorTests()
        {
            _cancellationToken = _cancellationTokenSource.Token;

            var so = new SearchOptions();
            so.UnsupportedSearchParams = new Tuple<string, string>[0];

            _searchOptionsFactory = Substitute.For<ISearchOptionsFactory>();
            _searchOptionsFactory.Create(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>()).Returns(so);

            _fhirDataStore = Substitute.For<IFhirDataStore>();

            // for an 'existing list' return a list with Patients
            _fhirDataStore.GetAsync(Arg.Is<ResourceKey>(x => x.Id == "existing-list"), Arg.Any<CancellationToken>()).Returns(
                x =>
                {
                    var longList = Samples.GetDefaultList();
                    var rawResourceFactory = new RawResourceFactory(new FhirJsonSerializer());
                    return new ResourceWrapper(
                        longList,
                        rawResourceFactory.Create(longList, keepMeta: true),
                        new ResourceRequest(HttpMethod.Post, "http://fhir"),
                        false,
                        null,
                        null,
                        null);
                        });

            _scopedDataStore = Substitute.For<IScoped<IFhirDataStore>>();
            _scopedDataStore.Value.Returns(_fhirDataStore);

            _nonEmptyBundle = new Bundle
            {
                Type = Bundle.BundleType.Batch,
                Entry = new List<Bundle.EntryComponent>
                {
                    new Bundle.EntryComponent
                    {
                        Resource = Samples.GetDefaultObservation().ToPoco(),
                        Request = new Bundle.RequestComponent
                        {
                            Method = Bundle.HTTPVerb.POST,
                            Url = "Observation",
                        },
                    },
                    new Bundle.EntryComponent
                    {
                        Request = new Bundle.RequestComponent
                        {
                            Method = Bundle.HTTPVerb.GET,
                            Url = "Patient?name=peter",
                        },
                    },
                },
            }.ToResourceElement();

            _bundleFactory = Substitute.For<IBundleFactory>();
            _bundleFactory.CreateSearchBundle(Arg.Any<SearchResult>()).Returns(_nonEmptyBundle);
        }

        [Fact]
        public async Task GivenARequest_WhenNoListQuery_QueriesUnchanged()
        {
            var behavior = new ListSearchPipeBehavior(_searchOptionsFactory, _bundleFactory, _scopedDataStore, Deserializers.ResourceDeserializer, new ReferenceSearchValueParser(new FhirRequestContextAccessor()));

            string guid1 = Guid.NewGuid().ToString();
            string guid2 = Guid.NewGuid().ToString();
            IReadOnlyList<Tuple<string, string>> list =
            new[]
            {
                Tuple.Create("firstItem", guid1),
                Tuple.Create("secondItem", guid2),
            };

            var getResourceRequest = Substitute.For<SearchResourceRequest>("Patient", list);

            SearchResourceResponse response = await behavior.Handle(
                getResourceRequest,
                CancellationToken.None,
                () => { return Task.FromResult(new SearchResourceResponse(_nonEmptyBundle)); });

            Assert.Equal(_nonEmptyBundle, response.Bundle);

            Assert.Equal(2, getResourceRequest.Received().Queries.Count);
            Assert.Equal("firstItem", getResourceRequest.Received().Queries[0].Item1);
            Assert.Equal(guid1, getResourceRequest.Received().Queries[0].Item2);
            Assert.Equal("secondItem", getResourceRequest.Received().Queries[1].Item1);
            Assert.Equal(guid2, getResourceRequest.Received().Queries[1].Item2);
        }

        [Fact]
        public async Task GivenARequest_WhenListValueMissing_EmptyResultsReturned()
        {
            var behavior = new ListSearchPipeBehavior(_searchOptionsFactory, _bundleFactory, _scopedDataStore, Deserializers.ResourceDeserializer, new ReferenceSearchValueParser(new FhirRequestContextAccessor()));

            IReadOnlyList<Tuple<string, string>> list =
            new[]
            {
                Tuple.Create("_list", string.Empty),
                Tuple.Create("_tag", Guid.NewGuid().ToString()),
                Tuple.Create("_id", Guid.NewGuid().ToString()),
            };

            var getResourceRequest = Substitute.For<SearchResourceRequest>("Patient", list);
            SearchResourceResponse response = await behavior.Handle(
                getResourceRequest,
                CancellationToken.None,
                () =>
                {
                    return Task.FromResult(new SearchResourceResponse(_nonEmptyBundle));
                });

            Assert.Equal(_nonEmptyBundle, response.Bundle);
        }

        [Fact]
        public async Task GivenARequest_WhenListValueExistsButValueNotFound_EmptyResponseReturned()
        {
            var behavior =
                new ListSearchPipeBehavior(_searchOptionsFactory, _bundleFactory, _scopedDataStore, Deserializers.ResourceDeserializer, new ReferenceSearchValueParser(new FhirRequestContextAccessor()));

            IReadOnlyList<Tuple<string, string>> list =
            new[]
            {
                Tuple.Create("_list", Guid.NewGuid().ToString()),
                Tuple.Create("_tag", Guid.NewGuid().ToString()),
                Tuple.Create("_id", Guid.NewGuid().ToString()),
            };

            var getResourceRequest = Substitute.For<SearchResourceRequest>("Patient", list);
            Assert.True(getResourceRequest.Queries.Count == 3);

            SearchResourceResponse response = await behavior.Handle(
                getResourceRequest,
                CancellationToken.None,
                () =>
                {
                    return Task.FromResult(new SearchResourceResponse(_nonEmptyBundle));
                });

            var emptyResponse = behavior.CreateEmptySearchResponse(getResourceRequest);
            Assert.Equal(emptyResponse.Bundle, response.Bundle);
        }

        [Fact]
        public async Task GivenARequest_WhenListValueFound_ExpectedIdQueriesAdded()
        {
            var behavior =
                new ListSearchPipeBehavior(_searchOptionsFactory, _bundleFactory, _scopedDataStore, Deserializers.ResourceDeserializer, new ReferenceSearchValueParser(new FhirRequestContextAccessor()));

            IReadOnlyList<Tuple<string, string>> list = new[] { Tuple.Create("_list", "existing-list") };
            var getResourceRequest = Substitute.For<SearchResourceRequest>("Patient", list);

            Assert.True(getResourceRequest.Queries.Count == 1);
            Assert.Equal("_list", getResourceRequest.Queries[0].Item1);
            Assert.Equal("existing-list", getResourceRequest.Queries[0].Item2);

            SearchResourceResponse response = await behavior.Handle(
                getResourceRequest,
                CancellationToken.None,
                () =>
                {
                    return Task.FromResult(new SearchResourceResponse(_nonEmptyBundle));
                });

            Assert.Equal(1, getResourceRequest.Received().Queries.Count);
            Assert.Equal("_id", getResourceRequest.Received().Queries[0].Item1);
            Assert.Contains("pat1", getResourceRequest.Received().Queries[0].Item2);
            Assert.Contains("pat2", getResourceRequest.Received().Queries[0].Item2);
            Assert.Contains("pat3", getResourceRequest.Received().Queries[0].Item2);
        }
    }
}
