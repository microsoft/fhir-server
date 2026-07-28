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
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Filters;
using Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.SemanticSearch;
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
    public class SemanticSearchHandlerTests
    {
        private readonly ISearchService _searchService = Substitute.For<ISearchService>();
        private readonly IDocumentReferenceSemanticSearch _semanticSearch = Substitute.For<IDocumentReferenceSemanticSearch>();
        private readonly IDataResourceFilter _dataResourceFilter = Substitute.For<IDataResourceFilter>();

        [Fact]
        public async Task GivenPatientSemanticSearch_WhenHandled_ThenAllSupportedResourceTypesAreGloballyRanked()
        {
            const string patientReference = "Patient/123";
            ResourceWrapper documentReference = CreateResourceWrapper(new DocumentReference { Id = "document-reference" }, 101);
            ResourceWrapper observation = CreateResourceWrapper(new Observation { Id = "observation" }, 102);
            ResourceWrapper diagnosticReport = CreateResourceWrapper(new DiagnosticReport { Id = "diagnostic-report" }, 103);
            var candidatesByType = new Dictionary<string, ResourceWrapper>
            {
                [ResourceType.DocumentReference.ToString()] = documentReference,
                [ResourceType.Observation.ToString()] = observation,
                [ResourceType.DiagnosticReport.ToString()] = diagnosticReport,
            };

            _searchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                CancellationToken.None)
                .Returns(callInfo => CreateSearchResult(candidatesByType[callInfo.ArgAt<string>(0)]));
            _dataResourceFilter.Filter(Arg.Any<SearchResult>()).Returns(callInfo => callInfo.Arg<SearchResult>());
            _semanticSearch.SearchAsync(
                "breathing difficulty",
                Arg.Any<IReadOnlyList<ResourceWrapper>>(),
                3,
                CancellationToken.None)
                .Returns(new[]
                {
                    CreateVectorResult(observation, 0.95f),
                    CreateVectorResult(documentReference, 0.85f),
                    CreateVectorResult(diagnosticReport, 0.75f),
                });

            var handler = new SemanticSearchHandler(
                _searchService,
                _semanticSearch,
                DisabledFhirAuthorizationService.Instance,
                _dataResourceFilter,
                Deserializers.ResourceDeserializer,
                Options.Create(new VectorSearchConfiguration()));

            SemanticSearchResponse response = await handler.Handle(
                new SemanticSearchRequest("breathing difficulty", patientReference, 3),
                CancellationToken.None);

            Bundle bundle = response.Bundle.ToPoco<Bundle>();
            Assert.Equal(3, bundle.Total);
            Assert.Collection(
                bundle.Entry,
                entry => Assert.IsType<Observation>(entry.Resource),
                entry => Assert.IsType<DocumentReference>(entry.Resource),
                entry => Assert.IsType<DiagnosticReport>(entry.Resource));
            Assert.Equal(new decimal?[] { 0.95m, 0.85m, 0.75m }, bundle.Entry.Select(entry => entry.Search.Score).ToArray());
            foreach (Bundle.EntryComponent entry in bundle.Entry)
            {
                Extension evidence = Assert.Single(entry.Search.Extension, extension => extension.Url == SemanticSearchEvidence.ExtensionUrl);
                Assert.Equal("Matched passage", ((FhirString)evidence.Extension.Single(extension => extension.Url == SemanticSearchEvidence.TextExtensionUrl).Value).Value);
                Assert.Equal($"{entry.Resource.TypeName}.text", ((FhirString)evidence.Extension.Single(extension => extension.Url == SemanticSearchEvidence.SourcePathExtensionUrl).Value).Value);
                Assert.Equal(
                    $"{entry.Resource.TypeName}/{entry.Resource.Id}",
                    ((ResourceReference)evidence.Extension.Single(extension => extension.Url == SemanticSearchEvidence.SourceExtensionUrl).Value).Reference);
            }

            foreach (string resourceType in candidatesByType.Keys)
            {
                await _searchService.Received(1).SearchAsync(
                    resourceType,
                    Arg.Is<IReadOnlyList<Tuple<string, string>>>(parameters =>
                        parameters.Contains(Tuple.Create("patient", patientReference))),
                    CancellationToken.None);
            }

            await _semanticSearch.Received(1).SearchAsync(
                "breathing difficulty",
                Arg.Is<IReadOnlyList<ResourceWrapper>>(candidates => candidates.Count == 3),
                3,
                CancellationToken.None);
        }

        [Fact]
        public async Task GivenSelectedResourceTypes_WhenHandled_ThenOnlySelectedTypesAreSearched()
        {
            var emptyResult = new SearchResult(
                Array.Empty<SearchResultEntry>(),
                continuationToken: null,
                sortOrder: null,
                unsupportedSearchParameters: Array.Empty<Tuple<string, string>>());
            _searchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                CancellationToken.None)
                .Returns(emptyResult);
            _dataResourceFilter.Filter(emptyResult).Returns(emptyResult);
            _semanticSearch.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<ResourceWrapper>>(),
                Arg.Any<int>(),
                CancellationToken.None)
                .Returns(Array.Empty<VectorSearchResult>());
            var handler = new SemanticSearchHandler(
                _searchService,
                _semanticSearch,
                DisabledFhirAuthorizationService.Instance,
                _dataResourceFilter,
                Deserializers.ResourceDeserializer,
                Options.Create(new VectorSearchConfiguration()));

            await handler.Handle(
                new SemanticSearchRequest("breathing difficulty", "Patient/123", 3, new[] { "Observation" }),
                CancellationToken.None);

            await _searchService.Received(1).SearchAsync(
                "Observation",
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                CancellationToken.None);
            await _searchService.DidNotReceive().SearchAsync(
                "DocumentReference",
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                CancellationToken.None);
            await _searchService.DidNotReceive().SearchAsync(
                "DiagnosticReport",
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                CancellationToken.None);
        }

        private static SearchResult CreateSearchResult(ResourceWrapper resource)
        {
            return new SearchResult(
                new[] { new SearchResultEntry(resource) },
                continuationToken: null,
                sortOrder: null,
                unsupportedSearchParameters: Array.Empty<Tuple<string, string>>());
        }

        private static VectorSearchResult CreateVectorResult(ResourceWrapper resource, float score)
        {
            var evidence = new SemanticSearchEvidence(
                "Matched passage",
                0,
                new Uri($"https://example.org/fhir/SearchParameter/{resource.ResourceTypeName}-semantic"),
                $"{resource.ResourceTypeName}/{resource.ResourceId}",
                $"{resource.ResourceTypeName}.text");
            return new VectorSearchResult(resource.ResourceTypeName, resource.ResourceSurrogateId, score, evidence);
        }

        private static ResourceWrapper CreateResourceWrapper(Resource resource, long resourceSurrogateId)
        {
            resource.Meta = new Meta { VersionId = "1" };
            var serializer = new FhirJsonSerializer();
            return new ResourceWrapper(
                resource.ToResourceElement(),
                new RawResource(serializer.SerializeToString(resource), FhirResourceFormat.Json, isMetaSet: true),
                new ResourceRequest(HttpMethod.Post, "http://test/resource"),
                deleted: false,
                searchIndices: null,
                compartmentIndices: null,
                lastModifiedClaims: null,
                resourceSurrogateId: resourceSurrogateId);
        }
    }
}
