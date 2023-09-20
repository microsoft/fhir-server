// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Api.Features.Resources.Bundle;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Resources;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Resources.Bundle
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Bundle)]
    public class TransactionBundleValidatorTests
    {
        private readonly ISearchService _searchService = Substitute.For<ISearchService>();
        private readonly TransactionBundleValidator _transactionBundleValidator;
        private readonly Dictionary<string, (string resourceId, string resourceType)> _idDictionary;

        public TransactionBundleValidatorTests()
        {
            _transactionBundleValidator = new TransactionBundleValidator(new ResourceReferenceResolver(_searchService, new QueryStringParser()), Substitute.For<ILogger<TransactionBundleValidator>>());
            _idDictionary = new Dictionary<string, (string resourceId, string resourceType)>();
        }

        [Fact]
        public async Task GivenATransactionBundle_WhenContainsUniqueResources_ThenNoExceptionShouldBeThrown()
        {
            var requestBundle = Samples.GetJsonSample("Bundle-TransactionWithValidBundleEntry");
            await _transactionBundleValidator.ValidateBundle(requestBundle.ToPoco<Hl7.Fhir.Model.Bundle>(), _idDictionary, CancellationToken.None);

            ValidateIdDictionaryPopulatedCorrectly(_idDictionary, Array.Empty<Action<KeyValuePair<string, (string resourceId, string resourceType)>>>());
        }

        [Fact]
        public async Task GivenATransactionBundle_WhenContainsAnExistingResource_ThenIdDictionaryShouldBeUpdated()
        {
            var requestBundle = Samples.GetJsonSample("Bundle-TransactionWithValidBundleEntry");

            MockSearchAsync(1);

            await _transactionBundleValidator.ValidateBundle(requestBundle.ToPoco<Hl7.Fhir.Model.Bundle>(), _idDictionary, CancellationToken.None);

            var expectedEntries = new[]
            {
                new Action<KeyValuePair<string, (string resourceId, string resourceType)>>(keyValuePair =>
                {
                    (string key, (string resourceId, string resourceType)) = keyValuePair;

                    Assert.Equal("urn:uuid:88f151c0-a954-468a-88bd-5ae15c08e059", key);
                    Assert.Equal("1234", resourceId);
                    Assert.Equal("Patient", resourceType);
                }),
            };

            ValidateIdDictionaryPopulatedCorrectly(
                _idDictionary,
                expectedEntries);
        }

        [Fact]
        public async Task GivenATransactionBundle_WhenContainsMultipleMatchingExistingResource_ThenPreconditionFailedExceptionShouldBeThrown()
        {
            var requestBundle = Samples.GetJsonSample("Bundle-TransactionWithValidBundleEntry");

            MockSearchAsync(2);

            await Assert.ThrowsAsync<PreconditionFailedException>(async () => await _transactionBundleValidator.ValidateBundle(requestBundle.ToPoco<Hl7.Fhir.Model.Bundle>(), _idDictionary, CancellationToken.None));
        }

        [Theory]
        [InlineData("Bundle-TransactionWithConditionalReferenceReferringToSameResource", "Patient?identifier=http:/example.org/fhir/ids|234234")]
        [InlineData("Bundle-TransactionWithMultipleEntriesModifyingSameResource", "Patient/123")]
        public async Task GivenATransactionBundle_WhenContainsMultipleEntriesWithTheSameResource_ThenRequestNotValidExceptionShouldBeThrown(string inputBundle, string requestedUrlInErrorMessage)
        {
            var expectedMessage = "Bundle contains multiple entries that refers to the same resource '" + requestedUrlInErrorMessage + "'.";

            var requestBundle = Samples.GetJsonSample(inputBundle);
            var exception = await Assert.ThrowsAsync<RequestNotValidException>(() => ValidateIfBundleEntryIsUniqueAsync(requestBundle));
            Assert.Equal(expectedMessage, exception.Message);
        }

        [Fact]
        public async Task GivenATransactionBundle_WhenContainsEntryWithConditionalDelete_ThenRequestNotValidExceptionShouldBeThrown()
        {
            var expectedMessage = "Requested operation 'Patient?identifier=123456' is not supported using DELETE.";

            var requestBundle = Samples.GetDefaultTransaction();
            var exception = await Assert.ThrowsAsync<RequestNotValidException>(() => _transactionBundleValidator.ValidateBundle(requestBundle.ToPoco<Hl7.Fhir.Model.Bundle>(), _idDictionary, CancellationToken.None));
            Assert.Equal(expectedMessage, exception.Message);
        }

        private static void ValidateIdDictionaryPopulatedCorrectly(Dictionary<string, (string resourceId, string resourceType)> idDictionary, Action<KeyValuePair<string, (string resourceId, string resourceType)>>[] actions)
        {
            Assert.Collection(idDictionary, actions);
        }

        private async Task ValidateIfBundleEntryIsUniqueAsync(ResourceElement requestBundle)
        {
            var bundle = requestBundle.ToPoco<Hl7.Fhir.Model.Bundle>();

            SearchResult mockSearchResult = GenerateSearchResult(1);

            _searchService.SearchAsync("Patient", Arg.Any<IReadOnlyList<Tuple<string, string>>>(), CancellationToken.None).Returns(mockSearchResult);

            await _transactionBundleValidator.ValidateBundle(bundle, _idDictionary, CancellationToken.None);
        }

        private void MockSearchAsync(int resultCount)
        {
            SearchResult searchResult = GenerateSearchResult(resultCount);

            _searchService.SearchAsync(
                    Arg.Any<string>(),
                    Arg.Is<IReadOnlyList<Tuple<string, string>>>(l => l.Any(t => t.Item1 == "identifier" && t.Item2 == "234234")),
                    Arg.Any<CancellationToken>())
                .Returns(searchResult);
        }

        private static SearchResult GenerateSearchResult(int resultCount)
        {
            var result = new SearchResultEntry(
                new ResourceWrapper(
                    "1234",
                    "1",
                    "Patient",
                    new RawResource(
                        "data",
                        FhirResourceFormat.Unknown,
                        isMetaSet: false),
                    new ResourceRequest("POST"),
                    DateTimeOffset.UtcNow,
                    false,
                    null,
                    null,
                    null));

            var searchResult = new SearchResult(
                Enumerable.Repeat(result, resultCount),
                null,
                null,
                Array.Empty<Tuple<string, string>>());
            return searchResult;
        }
    }
}
