// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Resources;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using static Hl7.Fhir.Model.Bundle;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Resources
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DomainLogicValidation)]
    public class ResourceReferenceResolverTests
    {
        private readonly ISearchService _searchService = Substitute.For<ISearchService>();
        private readonly ResourceReferenceResolver _referenceResolver;

        public ResourceReferenceResolverTests()
        {
            _referenceResolver = new ResourceReferenceResolver(_searchService, new TestQueryStringParser());
        }

        [Fact]
        public async Task GivenATransactionBundleWithIdentifierReferences_WhenResolved_ThenReferencesValuesAreNotUpdated()
        {
            var observation = new Observation
            {
                Subject = new ResourceReference
                {
                    Identifier = new Identifier("https://example.com", "12345"),
                },
            };

            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Entry = new List<EntryComponent>
                {
                    new EntryComponent
                    {
                        Resource = observation,
                    },
                },
            };

            var referenceIdDictionary = new Dictionary<string, (string resourceId, string resourceType)>();

            foreach (var entry in bundle.Entry)
            {
                var references = entry.Resource.GetAllChildren<ResourceReference>().ToList();

                // Asserting the conditional reference value before resolution
                Assert.Null(references.First().Reference);
                var requestUrl = (entry.Request != null) ? entry.Request.Url : null;
                await _referenceResolver.ResolveReferencesAsync(entry.Resource, referenceIdDictionary, requestUrl, maxParalelism: false, CancellationToken.None);

                // Asserting the resolved reference value after resolution
                Assert.Null(references.First().Reference);
            }
        }

        [Fact]
        public async Task GivenATransactionBundleWithConditionalReferences_WhenResolved_ThenReferencesValuesAreUpdatedCorrectly()
        {
            var requestBundle = Samples.GetJsonSample("Bundle-TransactionWithConditionalReferenceInResourceBody");
            var bundle = requestBundle.ToPoco<Hl7.Fhir.Model.Bundle>();

            SearchResultEntry mockSearchEntry = GetMockSearchEntry("123", KnownResourceTypes.Patient);

            var searchResult = new SearchResult(new[] { mockSearchEntry }, null, null, new Tuple<string, string>[0]);
            _searchService.SearchAsync("Patient", Arg.Any<IReadOnlyList<Tuple<string, string>>>(), CancellationToken.None).Returns(searchResult);

            var referenceIdDictionary = new Dictionary<string, (string resourceId, string resourceType)>();

            foreach (var entry in bundle.Entry)
            {
                var references = entry.Resource.GetAllChildren<ResourceReference>().ToList();

                // Asserting the conditional reference value before resolution
                Assert.Equal("Patient?identifier=12345", references.First().Reference);

                var requestUrl = (entry.Request != null) ? entry.Request.Url : null;
                await _referenceResolver.ResolveReferencesAsync(entry.Resource, referenceIdDictionary, requestUrl, maxParalelism: false, CancellationToken.None);

                // Asserting the resolved reference value after resolution
                Assert.Equal("Patient/123", references.First().Reference);
            }
        }

        [Fact]
        public async Task GivenATransactionBundleWithConditionalReferences_WhenUsingMaxParallelism_ThenOptimizeConcurrencyParameterIsPresent()
        {
            // #conditionalQueryParallelism

            var requestBundle = Samples.GetJsonSample("Bundle-TransactionWithConditionalReferenceInResourceBody");
            var bundle = requestBundle.ToPoco<Hl7.Fhir.Model.Bundle>();

            SearchResultEntry mockSearchEntry = GetMockSearchEntry("2112", KnownResourceTypes.Patient);

            var searchResult = new SearchResult(new[] { mockSearchEntry }, null, null, new Tuple<string, string>[0]);
            _searchService
                .SearchAsync(
                    "Patient",
                    Arg.Do<IReadOnlyList<Tuple<string, string>>>(
                        p => p.Single(x => x.Item1 == KnownQueryParameterNames.OptimizeConcurrency)), // Checks if 'OptimizeConcurrency' is present as one of the query/search parameters.
                    CancellationToken.None)
                .Returns(searchResult);

            var referenceIdDictionary = new Dictionary<string, (string resourceId, string resourceType)>();

            foreach (var entry in bundle.Entry)
            {
                var references = entry.Resource.GetAllChildren<ResourceReference>().ToList();

                // Asserting the conditional reference value before resolution
                Assert.Equal("Patient?identifier=12345", references.First().Reference);

                var requestUrl = (entry.Request != null) ? entry.Request.Url : null;
                await _referenceResolver.ResolveReferencesAsync(
                    entry.Resource,
                    referenceIdDictionary,
                    requestUrl,
                    maxParalelism: true,
                    CancellationToken.None);

                // Asserting the resolved reference value after resolution
                Assert.Equal("Patient/2112", references.First().Reference);
            }
        }

        [Fact]
        public async Task GivenATransactionBundleWithConditionalReferences_WhenNotResolved_ThenRequestNotValidExceptionShouldBeThrown()
        {
            var requestBundle = Samples.GetJsonSample("Bundle-TransactionWithConditionalReferenceInResourceBody");
            var bundle = requestBundle.ToPoco<Hl7.Fhir.Model.Bundle>();

            SearchResultEntry mockSearchEntry = GetMockSearchEntry("123", KnownResourceTypes.Patient);
            SearchResultEntry mockSearchEntry1 = GetMockSearchEntry("123", KnownResourceTypes.Patient);

            var expectedMessage = "Given conditional reference 'Patient?identifier=12345' does not resolve to a resource.";

            var searchResult = new SearchResult(new[] { mockSearchEntry, mockSearchEntry1 }, null, null, new Tuple<string, string>[0]);
            _searchService.SearchAsync("Patient", Arg.Any<IReadOnlyList<Tuple<string, string>>>(), CancellationToken.None).Returns(searchResult);

            var referenceIdDictionary = new Dictionary<string, (string resourceId, string resourceType)>();
            foreach (var entry in bundle.Entry)
            {
                var requestUrl = (entry.Request != null) ? entry.Request.Url : null;
                var exception = await Assert.ThrowsAsync<RequestNotValidException>(() => _referenceResolver.ResolveReferencesAsync(
                    entry.Resource,
                    referenceIdDictionary,
                    requestUrl,
                    maxParalelism: false,
                    CancellationToken.None));
                Assert.Equal(exception.Message, expectedMessage);
            }
        }

        [Fact]
        public async Task GivenATransactionBundleWithInvalidResourceTypeInReference_WhenExecuted_ThenRequestNotValidExceptionShouldBeThrown()
        {
            var requestBundle = Samples.GetJsonSample("Bundle-TransactionWithInvalidResourceType");
            var bundle = requestBundle.ToPoco<Hl7.Fhir.Model.Bundle>();

            var expectedMessage = "Resource type 'Patientt' in the reference 'Patientt?identifier=12345' is not supported.";

            var referenceIdDictionary = new Dictionary<string, (string resourceId, string resourceType)>();
            foreach (var entry in bundle.Entry)
            {
                var requestUrl = (entry.Request != null) ? entry.Request.Url : null;
                var exception = await Assert.ThrowsAsync<RequestNotValidException>(() => _referenceResolver.ResolveReferencesAsync(
                    entry.Resource,
                    referenceIdDictionary,
                    requestUrl,
                    maxParalelism: false,
                    CancellationToken.None));
                Assert.Equal(exception.Message, expectedMessage);
            }
        }

        private static SearchResultEntry GetMockSearchEntry(string resourceId, string resourceType)
        {
            return new SearchResultEntry(
               new ResourceWrapper(
                   resourceId,
                   "1",
                   resourceType,
                   new RawResource("data", FhirResourceFormat.Json, isMetaSet: false),
                   null,
                   DateTimeOffset.MinValue,
                   false,
                   null,
                   null,
                   null));
        }
    }
}
