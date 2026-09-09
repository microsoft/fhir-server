// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.SemanticSearch
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SemanticSearchEvidenceFilterTests
    {
        private readonly ISearchService _searchService = Substitute.For<ISearchService>();
        private readonly IDataResourceFilter _dataResourceFilter = Substitute.For<IDataResourceFilter>();
        private readonly SemanticSearchEvidenceFilter _filter;

        public SemanticSearchEvidenceFilterTests()
        {
            _dataResourceFilter.Filter(Arg.Any<SearchResult>()).Returns(callInfo => callInfo.Arg<SearchResult>());
            _filter = new SemanticSearchEvidenceFilter(_searchService, _dataResourceFilter);
        }

        [Fact]
        public async Task GivenOwnerSourcedEvidence_WhenFiltered_ThenResultIsPreservedWithoutAdditionalSearch()
        {
            ResourceWrapper observation = CreateResourceWrapper("Observation", "observation", 1);
            SearchResult searchResult = CreateSearchResult(CreateSemanticEntry(observation, "Observation/observation/_history/1", 0.9m));

            SearchResult filtered = await _filter.FilterAsync(searchResult, CancellationToken.None);

            Assert.Same(searchResult, filtered);
            await _searchService.DidNotReceiveWithAnyArgs().SearchAsync(default, default, default);
        }

        [Fact]
        public async Task GivenAuthorizedExternalSource_WhenFiltered_ThenResultAndEvidenceArePreserved()
        {
            ResourceWrapper owner = CreateResourceWrapper("DocumentReference", "document", 1);
            ResourceWrapper source = CreateResourceWrapper("Binary", "source", 2);
            SearchResult sourceSearchResult = CreateSearchResult(new SearchResultEntry(source));
            _searchService.SearchAsync(
                "Binary",
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                CancellationToken.None)
                .Returns(sourceSearchResult);

            SearchResult filtered = await _filter.FilterAsync(
                CreateSearchResult(CreateSemanticEntry(owner, "Binary/source/_history/1", 0.9m)),
                CancellationToken.None);

            SearchResultEntry result = Assert.Single(filtered.Results);
            Assert.Equal(0.9m, result.Score);
            Assert.Equal(1, Assert.Single(result.EvidenceItems).Rank);
            await _searchService.Received(1).SearchAsync(
                "Binary",
                Arg.Is<IReadOnlyList<Tuple<string, string>>>(parameters => parameters.Single().Item1 == "_id" && parameters.Single().Item2 == "source"),
                CancellationToken.None);
        }

        [Fact]
        public async Task GivenDeniedExternalSource_WhenFiltered_ThenResultScoreEvidenceAndTotalAreRemoved()
        {
            ResourceWrapper owner = CreateResourceWrapper("DocumentReference", "document", 1);
            _searchService.SearchAsync(
                "Binary",
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                CancellationToken.None)
                .Returns(SearchResult.Empty());

            SearchResult filtered = await _filter.FilterAsync(
                CreateSearchResult(CreateSemanticEntry(owner, "Binary/source/_history/1", 0.9m)),
                CancellationToken.None);

            Assert.Empty(filtered.Results);
            Assert.Null(filtered.TotalCount);
        }

        [Fact]
        public async Task GivenAuthorizedWitnessAndSource_WhenFiltered_ThenResultIsPreserved()
        {
            ResourceWrapper root = CreateResourceWrapper("Patient", "patient", 1);
            ResourceWrapper witness = CreateResourceWrapper("DocumentReference", "document", 2);
            ResourceWrapper source = CreateResourceWrapper("Binary", "source", 3);
            _searchService.SearchAsync("DocumentReference", Arg.Any<IReadOnlyList<Tuple<string, string>>>(), CancellationToken.None)
                .Returns(CreateSearchResult(new SearchResultEntry(witness)));
            _searchService.SearchAsync("Binary", Arg.Any<IReadOnlyList<Tuple<string, string>>>(), CancellationToken.None)
                .Returns(CreateSearchResult(new SearchResultEntry(source)));

            SearchResult filtered = await _filter.FilterAsync(
                CreateSearchResult(CreateSemanticEntry(
                    root,
                    0.9m,
                    CreateEvidence("Binary/source/_history/1", 0.9m, "DocumentReference/document/_history/1"))),
                CancellationToken.None);

            Assert.Single(filtered.Results);
            await _searchService.Received(1).SearchAsync("DocumentReference", Arg.Any<IReadOnlyList<Tuple<string, string>>>(), CancellationToken.None);
            await _searchService.Received(1).SearchAsync("Binary", Arg.Any<IReadOnlyList<Tuple<string, string>>>(), CancellationToken.None);
        }

        [Fact]
        public async Task GivenDeniedWitnessAndAuthorizedSource_WhenFiltered_ThenWholeResultIsRemoved()
        {
            ResourceWrapper root = CreateResourceWrapper("Patient", "patient", 1);
            ResourceWrapper source = CreateResourceWrapper("Binary", "source", 3);
            _searchService.SearchAsync("DocumentReference", Arg.Any<IReadOnlyList<Tuple<string, string>>>(), CancellationToken.None)
                .Returns(SearchResult.Empty());
            _searchService.SearchAsync("Binary", Arg.Any<IReadOnlyList<Tuple<string, string>>>(), CancellationToken.None)
                .Returns(CreateSearchResult(new SearchResultEntry(source)));

            SearchResult filtered = await _filter.FilterAsync(
                CreateSearchResult(CreateSemanticEntry(
                    root,
                    0.9m,
                    CreateEvidence("Binary/source/_history/1", 0.9m, "DocumentReference/document/_history/1"))),
                CancellationToken.None);

            Assert.Empty(filtered.Results);
        }

        [Fact]
        public async Task GivenMixedAuthorizedAndDeniedSources_WhenFiltered_ThenWholeResultIsRemoved()
        {
            ResourceWrapper owner = CreateResourceWrapper("DocumentReference", "document", 1);
            ResourceWrapper allowedSource = CreateResourceWrapper("Binary", "allowed", 2);
            _searchService.SearchAsync(
                "Binary",
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                CancellationToken.None)
                .Returns(CreateSearchResult(new SearchResultEntry(allowedSource)));
            SearchResultEntry result = CreateSemanticEntry(
                owner,
                0.9m,
                CreateEvidence("Binary/allowed/_history/1", 0.9m),
                CreateEvidence("Binary/denied/_history/1", 0.8m));

            SearchResult filtered = await _filter.FilterAsync(CreateSearchResult(result), CancellationToken.None);

            Assert.Empty(filtered.Results);
        }

        [Theory]
        [InlineData("https://example.org/fhir/Binary/source")]
        [InlineData("Binary/source/_history")]
        public async Task GivenInvalidSourceReference_WhenFiltered_ThenResultIsRemovedWithoutSourceSearch(string sourceReference)
        {
            ResourceWrapper owner = CreateResourceWrapper("DocumentReference", "document", 1);

            SearchResult filtered = await _filter.FilterAsync(
                CreateSearchResult(CreateSemanticEntry(owner, sourceReference, 0.9m)),
                CancellationToken.None);

            Assert.Empty(filtered.Results);
            await _searchService.DidNotReceiveWithAnyArgs().SearchAsync(default, default, default);
        }

        [Fact]
        public async Task GivenInvalidWitnessReference_WhenFiltered_ThenResultIsRemovedWithoutSourceSearch()
        {
            ResourceWrapper root = CreateResourceWrapper("Patient", "patient", 1);

            SearchResult filtered = await _filter.FilterAsync(
                CreateSearchResult(CreateSemanticEntry(
                    root,
                    0.9m,
                    CreateEvidence("Binary/source/_history/1", 0.9m, "DocumentReference/document/_history"))),
                CancellationToken.None);

            Assert.Empty(filtered.Results);
            await _searchService.DidNotReceiveWithAnyArgs().SearchAsync(default, default, default);
        }

        [Fact]
        public async Task GivenUnsupportedSourceResourceType_WhenFiltered_ThenResultIsRemoved()
        {
            ResourceWrapper owner = CreateResourceWrapper("DocumentReference", "document", 1);
            _searchService.SearchAsync(
                "Unknown",
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                CancellationToken.None)
                .Returns<Task<SearchResult>>(_ => throw new ResourceNotSupportedException("Unknown"));

            SearchResult filtered = await _filter.FilterAsync(
                CreateSearchResult(CreateSemanticEntry(owner, "Unknown/source", 0.9m)),
                CancellationToken.None);

            Assert.Empty(filtered.Results);
        }

        [Fact]
        public async Task GivenUnauthorizedSourceSearch_WhenFiltered_ThenResultIsRemoved()
        {
            ResourceWrapper owner = CreateResourceWrapper("DocumentReference", "document", 1);
            _searchService.SearchAsync(
                "Binary",
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                CancellationToken.None)
                .Returns<Task<SearchResult>>(_ => throw new UnauthorizedFhirActionException());

            SearchResult filtered = await _filter.FilterAsync(
                CreateSearchResult(CreateSemanticEntry(owner, "Binary/source", 0.9m)),
                CancellationToken.None);

            Assert.Empty(filtered.Results);
        }

        private static SearchResultEntry CreateSemanticEntry(ResourceWrapper owner, string sourceReference, decimal score)
        {
            return CreateSemanticEntry(owner, score, CreateEvidence(sourceReference, score));
        }

        private static SearchResultEntry CreateSemanticEntry(ResourceWrapper owner, decimal score, params SemanticSearchEvidence[] evidence)
        {
            return new SearchResultEntry(owner, score: score, evidenceItems: evidence);
        }

        private static SemanticSearchEvidence CreateEvidence(string sourceReference, decimal score, string witnessReference = null)
        {
            return new SemanticSearchEvidence(
                "Matched passage",
                chunkOrdinal: 0,
                score,
                new Uri("https://example.org/fhir/SearchParameter/semantic-text"),
                sourceReference,
                "Binary.data",
                witnessReference: witnessReference);
        }

        private static SearchResult CreateSearchResult(params SearchResultEntry[] results)
        {
            return new SearchResult(
                results,
                continuationToken: null,
                sortOrder: null,
                unsupportedSearchParameters: Array.Empty<Tuple<string, string>>())
            {
                TotalCount = results.Length,
            };
        }

        private static ResourceWrapper CreateResourceWrapper(string resourceType, string resourceId, long resourceSurrogateId)
        {
            return new ResourceWrapper(
                resourceId,
                versionId: "1",
                resourceType,
                new RawResource(new Lazy<string>(() => $"{{\"resourceType\":\"{resourceType}\",\"id\":\"{resourceId}\"}}"), FhirResourceFormat.Json, isMetaSet: true),
                new ResourceRequest(HttpMethod.Post, "http://test/resource"),
                DateTimeOffset.UtcNow,
                deleted: false,
                searchIndices: null,
                compartmentIndices: null,
                lastModifiedClaims: null,
                resourceSurrogateId: resourceSurrogateId);
        }
    }
}
