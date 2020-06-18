// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.PreProcessors;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Validation
{
    public class ListSearchPreProcessorTests
    {
        private readonly ResourceDeserializer _resourceDeserializer;

        private readonly FhirJsonParser _fhirJsonParser = new FhirJsonParser();

        private readonly IFhirDataStore _fhirDataStore;

        private IScoped<IFhirDataStore> scope;

        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        private readonly CancellationToken _cancellationToken;

        public ListSearchPreProcessorTests()
        {
            _cancellationToken = _cancellationTokenSource.Token;

            _resourceDeserializer = new ResourceDeserializer(
               (FhirResourceFormat.Json, new Func<string, string, DateTimeOffset, ResourceElement>((str, version, lastUpdated) => _fhirJsonParser.Parse(str).ToResourceElement())));

            _fhirDataStore = Substitute.For<IFhirDataStore>();

            // for an 'existing list' return a list with Patients
            _fhirDataStore.GetAsync(Arg.Is<ResourceKey>(x => x.Id == "existing-list"), Arg.Any<CancellationToken>()).Returns(
                x =>
                {
                    var longList = Samples.GetDefaultList();
                    var rawResourceFactory = new RawResourceFactory(new FhirJsonSerializer());
                    return new ResourceWrapper(
                        longList,
                        rawResourceFactory.Create(longList),
                        new ResourceRequest(HttpMethod.Post, "http://fhir"),
                        false,
                        null,
                        null,
                        null);
                        });

            scope = Substitute.For<IScoped<IFhirDataStore>>();
            scope.Value.Returns(_fhirDataStore);
        }

        private ResourceKey<Hl7.Fhir.Model.List> CreateArg(string lstName)
        {
            return new ResourceKey<Hl7.Fhir.Model.List>(lstName);
        }

        [Fact]
        public async Task GivenARequest_WhenNoListQuery_QueriesUnchanged()
        {
            var preProcessor = new ListSearchPreProcessor(scope, _resourceDeserializer, new ReferenceSearchValueParser(new FhirRequestContextAccessor()));

            IReadOnlyList<Tuple<string, string>> list =
            new[]
            {
                Tuple.Create("firstItem", Guid.NewGuid().ToString()),
                Tuple.Create("secondItem", Guid.NewGuid().ToString()),
            };
            var getResourceRequest = new SearchResourceRequest("Patient", list);

            await preProcessor.Process(getResourceRequest, CancellationToken.None);
            Assert.True(getResourceRequest.Queries.Count == 2);
        }

        [Fact]
        public async Task GivenARequest_WhenListValueMissing_ListQueryRemoved()
        {
            var preProcessor = new ListSearchPreProcessor(scope, _resourceDeserializer, new ReferenceSearchValueParser(new FhirRequestContextAccessor()));

            IReadOnlyList<Tuple<string, string>> list =
            new[]
            {
                Tuple.Create("_list", string.Empty),
                Tuple.Create("_tag", Guid.NewGuid().ToString()),
                Tuple.Create("_id", Guid.NewGuid().ToString()),
            };

            var getResourceRequest = new SearchResourceRequest("Patient", list);

            Assert.True(getResourceRequest.Queries.Count == 3);
            await preProcessor.Process(getResourceRequest, CancellationToken.None);
            Assert.True(getResourceRequest.Queries.Count == 2);
        }

        [Fact]
        public async Task GivenARequest_WhenListValueExistsButValueNotFound_ListQueryRemovedAndFalseQueryAdded()
        {
            var preProcessor =
            new ListSearchPreProcessor(scope, _resourceDeserializer, new ReferenceSearchValueParser(new FhirRequestContextAccessor()));

            IReadOnlyList<Tuple<string, string>> list =
            new[]
            {
                Tuple.Create("_list", Guid.NewGuid().ToString()),
                Tuple.Create("_tag", Guid.NewGuid().ToString()),
                Tuple.Create("_id", Guid.NewGuid().ToString()),
            };

            var getResourceRequest = new SearchResourceRequest("Patient", list);

            Assert.True(getResourceRequest.Queries.Count == 3);

            await preProcessor.Process(getResourceRequest, CancellationToken.None);

            Assert.True(getResourceRequest.Queries.Count == 4);
            Assert.Equal("_id", getResourceRequest.Queries[2].Item1);
            Assert.Equal("_id", getResourceRequest.Queries[3].Item1);
            Assert.NotEqual(getResourceRequest.Queries[2].Item2, getResourceRequest.Queries[3].Item2);
        }

        [Fact]
        public async Task GivenARequest_WhenListValueFound_ExpectedIdQueriesAdded()
        {
            var preProcessor = new ListSearchPreProcessor(scope, _resourceDeserializer, new ReferenceSearchValueParser(new FhirRequestContextAccessor()));

            IReadOnlyList<Tuple<string, string>> list = new[] { Tuple.Create("_list", "existing-list") };
            var getResourceRequest = new SearchResourceRequest("Patient", list);

            Assert.True(getResourceRequest.Queries.Count == 1);
            Assert.Equal("_list", getResourceRequest.Queries[0].Item1);
            Assert.Equal("existing-list", getResourceRequest.Queries[0].Item2);

            await preProcessor.Process(getResourceRequest, _cancellationToken);

            Assert.Equal("_id", getResourceRequest.Queries[0].Item1);
            Assert.Contains("pat1", getResourceRequest.Queries[0].Item2);
            Assert.Contains("pat2", getResourceRequest.Queries[0].Item2);
            Assert.Contains("pat3", getResourceRequest.Queries[0].Item2);
        }
    }
}
