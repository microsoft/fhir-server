// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
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
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Validation
{
    public class ListSearchPreProcessorTests
    {
        private readonly ResourceDeserializer _resourceDeserializer;

        // private readonly FhirJsonSerializer _fhirJsonSerializer = new FhirJsonSerializer();
        private readonly FhirJsonParser _fhirJsonParser = new FhirJsonParser();

        private readonly IFhirDataStore _fhirDataStore;

        // private readonly ISearchService _searchService;
        private IScoped<IFhirDataStore> scope;

        public ListSearchPreProcessorTests()
        {
            _resourceDeserializer = new ResourceDeserializer(
               (FhirResourceFormat.Json, new Func<string, string, DateTimeOffset, ResourceElement>((str, version, lastUpdated) => _fhirJsonParser.Parse(str).ToResourceElement())));

            _fhirDataStore = Substitute.For<IFhirDataStore>();

            scope = Substitute.For<IScoped<IFhirDataStore>>();
            scope.Value.Returns(_fhirDataStore);
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
            var getResourceRequest = new SearchResourceRequest("Observation", list);

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

            var getResourceRequest = new SearchResourceRequest("Observation", list);

            Assert.True(getResourceRequest.Queries.Count == 3);
            await preProcessor.Process(getResourceRequest, CancellationToken.None);
            Assert.True(getResourceRequest.Queries.Count == 2);
        }

        [Fact]
        public async Task GivenARequest_WhenListValueExistsButValueNotFound_ListQueryRemovedAndFalseQueryAdded()
        {
            var preProcessor = new ListSearchPreProcessor(scope, _resourceDeserializer, new ReferenceSearchValueParser(new FhirRequestContextAccessor()));

            IReadOnlyList<Tuple<string, string>> list =
            new[]
            {
                Tuple.Create("_list", Guid.NewGuid().ToString()),
                Tuple.Create("_tag", Guid.NewGuid().ToString()),
                Tuple.Create("_id", Guid.NewGuid().ToString()),
            };

            var getResourceRequest = new SearchResourceRequest("Observation", list);

            Assert.True(getResourceRequest.Queries.Count == 3);
            await preProcessor.Process(getResourceRequest, CancellationToken.None);
            Assert.True(getResourceRequest.Queries.Count == 4);
        }

        [Fact]
        public async Task GivenARequest_WhenListValueFound_ExpectedIdQueriesAdded()
        {
            var preProcessor = new ListSearchPreProcessor(scope, _resourceDeserializer, new ReferenceSearchValueParser(new FhirRequestContextAccessor()));

            IReadOnlyList<Tuple<string, string>> list = new[] { Tuple.Create("_list", Guid.NewGuid().ToString()) };
            var getResourceRequest = new SearchResourceRequest("Observation", list);

            await preProcessor.Process(getResourceRequest, CancellationToken.None);
        }
    }
}
