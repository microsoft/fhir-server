// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        private readonly ILogger<TransactionBundleValidator> _logger = Substitute.For<ILogger<TransactionBundleValidator>>();
        private readonly ILogger<ResourceReferenceResolver> _loggerResourceReferenceResolver = Substitute.For<ILogger<ResourceReferenceResolver>>();
        private readonly TransactionBundleValidator _transactionBundleValidator;
        private readonly Dictionary<string, (string resourceId, string resourceType)> _idDictionary;

        public TransactionBundleValidatorTests()
        {
            _transactionBundleValidator = new TransactionBundleValidator(new ResourceReferenceResolver(_searchService, new QueryStringParser(), _loggerResourceReferenceResolver), _logger);
            _idDictionary = new Dictionary<string, (string resourceId, string resourceType)>();
        }

        [Theory]
        [InlineData(BundleProcessingLogic.Sequential)]
        [InlineData(BundleProcessingLogic.Parallel)]

        public async Task GivenATransactionBundle_WhenContainsUniqueResources_ThenNoExceptionShouldBeThrown(BundleProcessingLogic processingLogic)
        {
            var requestBundle = Samples.GetJsonSample("Bundle-TransactionWithValidBundleEntry");
            await _transactionBundleValidator.ValidateBundle(requestBundle.ToPoco<Hl7.Fhir.Model.Bundle>(), processingLogic, _idDictionary, CancellationToken.None);

            ValidateIdDictionaryPopulatedCorrectly(_idDictionary, Array.Empty<Action<KeyValuePair<string, (string resourceId, string resourceType)>>>());
        }

        [Theory]
        [InlineData(BundleProcessingLogic.Sequential)]
        [InlineData(BundleProcessingLogic.Parallel)]
        public async Task GivenATransactionBundle_WhenContainsAnExistingResource_ThenIdDictionaryShouldBeUpdated(BundleProcessingLogic processingLogic)
        {
            var requestBundle = Samples.GetJsonSample("Bundle-TransactionWithValidBundleEntry");

            MockSearchAsync(1);

            await _transactionBundleValidator.ValidateBundle(requestBundle.ToPoco<Hl7.Fhir.Model.Bundle>(), processingLogic, _idDictionary, CancellationToken.None);

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

        [Theory]
        [InlineData(BundleProcessingLogic.Sequential)]
        [InlineData(BundleProcessingLogic.Parallel)]
        public async Task GivenATransactionBundle_WhenContainsMultipleMatchingExistingResource_ThenPreconditionFailedExceptionShouldBeThrown(BundleProcessingLogic processingLogic)
        {
            var requestBundle = Samples.GetJsonSample("Bundle-TransactionWithValidBundleEntry");

            MockSearchAsync(2);

            await Assert.ThrowsAsync<PreconditionFailedException>(async () => await _transactionBundleValidator.ValidateBundle(
                requestBundle.ToPoco<Hl7.Fhir.Model.Bundle>(),
                processingLogic,
                _idDictionary,
                CancellationToken.None));
        }

        [Theory]
        [InlineData(BundleProcessingLogic.Sequential, "Bundle-TransactionWithConditionalReferenceReferringToSameResource", "Patient?identifier=http:/example.org/fhir/ids|234234")]
        [InlineData(BundleProcessingLogic.Sequential, "Bundle-TransactionWithMultipleEntriesModifyingSameResource", "Patient/123")]
        [InlineData(BundleProcessingLogic.Parallel, "Bundle-TransactionWithConditionalReferenceReferringToSameResource", "Patient?identifier=http:/example.org/fhir/ids|234234")]
        [InlineData(BundleProcessingLogic.Parallel, "Bundle-TransactionWithMultipleEntriesModifyingSameResource", "Patient/123")]
        public async Task GivenATransactionBundle_WhenContainsMultipleEntriesWithTheSameResource_ThenRequestNotValidExceptionShouldBeThrown(BundleProcessingLogic processingLogic, string inputBundle, string requestedUrlInErrorMessage)
        {
            var expectedMessage = "Bundle contains multiple entries that refers to the same resource '" + requestedUrlInErrorMessage + "'.";

            var requestBundle = Samples.GetJsonSample(inputBundle);
            var exception = await Assert.ThrowsAsync<RequestNotValidException>(() => ValidateIfBundleEntryIsUniqueAsync(requestBundle, processingLogic));
            Assert.Equal(expectedMessage, exception.Message);
        }

        [Theory]
        [InlineData(BundleProcessingLogic.Sequential)]
        [InlineData(BundleProcessingLogic.Parallel)]
        public async Task GivenATransactionBundle_WhenContainsEntryWithConditionalDelete_ThenRequestNotValidExceptionShouldBeThrown(BundleProcessingLogic processingLogic)
        {
            var expectedMessage = "Requested operation 'Patient?identifier=123456' is not supported using DELETE.";

            var requestBundle = Samples.GetDefaultTransaction();
            var exception = await Assert.ThrowsAsync<RequestNotValidException>(() => _transactionBundleValidator.ValidateBundle(
                requestBundle.ToPoco<Hl7.Fhir.Model.Bundle>(),
                processingLogic,
                _idDictionary,
                CancellationToken.None));
            Assert.Equal(expectedMessage, exception.Message);
        }

        [Theory]
        [InlineData(BundleProcessingLogic.Sequential)]
        [InlineData(BundleProcessingLogic.Parallel)]
        public async Task GivenATransactionBundle_WhenContainsMetaHistoryEntry_ThenNoExceptionShouldBeThrown(BundleProcessingLogic processingLogic)
        {
            var requestBundle = Samples.GetJsonSample("Bundle-TransactionWithMetaHistory");
            await _transactionBundleValidator.ValidateBundle(
                requestBundle.ToPoco<Hl7.Fhir.Model.Bundle>(),
                processingLogic,
                _idDictionary,
                CancellationToken.None);
        }

        [Theory]
        [InlineData(BundleProcessingLogic.Sequential)]
        public async Task GivenATransactionBundle_WhenContainsEntryWithHardDelete_ThenNoExceptionShouldBeThrown(BundleProcessingLogic processingLogic)
        {
            // TODO: When Parallel Bundles start supporting hard deletes, remove this test and update the Parallel test above.
            // TODO: 182638 - Add support to hard deletes in parallel processing mode.

            // Arrange
            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Type = Hl7.Fhir.Model.Bundle.BundleType.Transaction,
                Entry = new List<Hl7.Fhir.Model.Bundle.EntryComponent>
                {
                    new Hl7.Fhir.Model.Bundle.EntryComponent
                    {
                        Request = new Hl7.Fhir.Model.Bundle.RequestComponent
                        {
                            Method = Hl7.Fhir.Model.Bundle.HTTPVerb.DELETE,
                            Url = "Patient/123?_hardDelete=true",
                        },
                        Resource = new Hl7.Fhir.Model.Patient { Id = "123" },
                    },
                },
            };

            // Act & Assert - Should not throw
            await _transactionBundleValidator.ValidateBundle(bundle, processingLogic, _idDictionary, CancellationToken.None);
        }

        [Theory]
        [InlineData(BundleProcessingLogic.Parallel)]
        public async Task GivenATransactionBundle_WhenContainsEntryWithHardDelete_ThenExceptionShouldBeThrownWhenExecutedInParallel(BundleProcessingLogic processingLogic)
        {
            // Arrange
            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Type = Hl7.Fhir.Model.Bundle.BundleType.Transaction,
                Entry = new List<Hl7.Fhir.Model.Bundle.EntryComponent>
                {
                    new Hl7.Fhir.Model.Bundle.EntryComponent
                    {
                        Request = new Hl7.Fhir.Model.Bundle.RequestComponent
                        {
                            Method = Hl7.Fhir.Model.Bundle.HTTPVerb.DELETE,
                            Url = "Patient/123?_hardDelete=true",
                        },
                        Resource = new Hl7.Fhir.Model.Patient { Id = "123" },
                    },
                },
            };

            // Act & Assert - Should not throw
            await Assert.ThrowsAsync<RequestNotValidException>(async () =>
            {
                await _transactionBundleValidator.ValidateBundle(bundle, processingLogic, _idDictionary, CancellationToken.None);
            });
        }

        [Theory]
        [InlineData(BundleProcessingLogic.Sequential, "Patient?identifier=123456", "Requested operation 'Patient?identifier=123456' is not supported using DELETE.")]
        [InlineData(BundleProcessingLogic.Sequential, "Patient?name=John", "Requested operation 'Patient?name=John' is not supported using DELETE.")]
        [InlineData(BundleProcessingLogic.Sequential, "Observation?code=12345", "Requested operation 'Observation?code=12345' is not supported using DELETE.")]
        [InlineData(BundleProcessingLogic.Parallel, "Patient?identifier=123456", "Requested operation 'Patient?identifier=123456' is not supported using DELETE.")]
        [InlineData(BundleProcessingLogic.Parallel, "Patient?name=John", "Requested operation 'Patient?name=John' is not supported using DELETE.")]
        [InlineData(BundleProcessingLogic.Parallel, "Observation?code=12345", "Requested operation 'Observation?code=12345' is not supported using DELETE.")]
        public async Task GivenATransactionBundle_WhenContainsConditionalDelete_ThenRequestNotValidExceptionShouldBeThrown(BundleProcessingLogic processingLogic, string requestUrl, string expectedMessage)
        {
            // Arrange
            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Type = Hl7.Fhir.Model.Bundle.BundleType.Transaction,
                Entry = new List<Hl7.Fhir.Model.Bundle.EntryComponent>
                {
                    new Hl7.Fhir.Model.Bundle.EntryComponent
                    {
                        Request = new Hl7.Fhir.Model.Bundle.RequestComponent
                        {
                            Method = Hl7.Fhir.Model.Bundle.HTTPVerb.DELETE,
                            Url = requestUrl,
                        },
                        Resource = new Hl7.Fhir.Model.Patient(),
                    },
                },
            };

            // Act & Assert
            var exception = await Assert.ThrowsAsync<RequestNotValidException>(
                () => _transactionBundleValidator.ValidateBundle(bundle, processingLogic, _idDictionary, CancellationToken.None));
            Assert.Equal(expectedMessage, exception.Message);
        }

        [Theory]
        [InlineData(BundleProcessingLogic.Sequential, "Patient/123?_hardDelete=true")]
        [InlineData(BundleProcessingLogic.Sequential, "Observation/456?_purge=true")]
        [InlineData(BundleProcessingLogic.Sequential, "Patient/789?_hardDelete=true&_cascade=delete")]
        public async Task GivenATransactionBundle_WhenContainsDeleteWithResourceIdAndQueryParams_ThenNoExceptionShouldBeThrown(BundleProcessingLogic processingLogic, string requestUrl)
        {
            // Arrange - These are hard deletes with resource IDs and query parameters
            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Type = Hl7.Fhir.Model.Bundle.BundleType.Transaction,
                Entry = new List<Hl7.Fhir.Model.Bundle.EntryComponent>
                {
                    new Hl7.Fhir.Model.Bundle.EntryComponent
                    {
                        Request = new Hl7.Fhir.Model.Bundle.RequestComponent
                        {
                            Method = Hl7.Fhir.Model.Bundle.HTTPVerb.DELETE,
                            Url = requestUrl,
                        },
                        Resource = new Hl7.Fhir.Model.Patient(),
                    },
                },
            };

            // Act & Assert - Should not throw
            await _transactionBundleValidator.ValidateBundle(bundle, processingLogic, _idDictionary, CancellationToken.None);
        }

        [Theory]
        [InlineData(BundleProcessingLogic.Parallel, "Observation/456?_purge=true")]
        [InlineData(BundleProcessingLogic.Parallel, "Patient/123?_hardDelete=true")]
        [InlineData(BundleProcessingLogic.Parallel, "Patient/789?_hardDelete=true&_cascade=delete")]
        public async Task GivenATransactionBundle_WhenContainsDeleteWithResourceIdAndQueryParams_ThenExceptionShouldBeThrownWhenInParallel(BundleProcessingLogic processingLogic, string requestUrl)
        {
            // TODO: When Parallel Bundles start supporting hard deletes, remove this test and update the Parallel test above.
            // TODO: 182638 - Add support to hard deletes in parallel processing mode.

            // Arrange - These are hard deletes with resource IDs and query parameters
            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Type = Hl7.Fhir.Model.Bundle.BundleType.Transaction,
                Entry = new List<Hl7.Fhir.Model.Bundle.EntryComponent>
                {
                    new Hl7.Fhir.Model.Bundle.EntryComponent
                    {
                        Request = new Hl7.Fhir.Model.Bundle.RequestComponent
                        {
                            Method = Hl7.Fhir.Model.Bundle.HTTPVerb.DELETE,
                            Url = requestUrl,
                        },
                        Resource = new Hl7.Fhir.Model.Patient(),
                    },
                },
            };

            // As the request is for a parallel bundle, then exceptions should be thrown.
            await Assert.ThrowsAsync<RequestNotValidException>(async () => { await _transactionBundleValidator.ValidateBundle(bundle, processingLogic, _idDictionary, CancellationToken.None); });
        }

        [Theory]
        [InlineData(BundleProcessingLogic.Sequential, "")]
        [InlineData(BundleProcessingLogic.Sequential, "?test")]
        [InlineData(BundleProcessingLogic.Parallel, "")]
        [InlineData(BundleProcessingLogic.Parallel, "?test")]
        public async Task GivenATransactionBundle_WhenUrlIsNotWellFormed_ThenRequestNotValidExceptionShouldBeThrown(BundleProcessingLogic processingLogic, string invalidUrl)
        {
            var bundle = Samples.GetBasicTransactionBundleWithSingleResource();

            bundle.Entry.First().Request = new Hl7.Fhir.Model.Bundle.RequestComponent()
            {
                Method = Hl7.Fhir.Model.Bundle.HTTPVerb.PUT,
                Url = invalidUrl,
            };

            await Assert.ThrowsAsync<RequestNotValidException>(() => _transactionBundleValidator.ValidateBundle(bundle, processingLogic, _idDictionary, CancellationToken.None));
        }

        private static void ValidateIdDictionaryPopulatedCorrectly(Dictionary<string, (string resourceId, string resourceType)> idDictionary, Action<KeyValuePair<string, (string resourceId, string resourceType)>>[] actions)
        {
            Assert.Collection(idDictionary, actions);
        }

        private async Task ValidateIfBundleEntryIsUniqueAsync(ResourceElement requestBundle, BundleProcessingLogic processingLogic)
        {
            var bundle = requestBundle.ToPoco<Hl7.Fhir.Model.Bundle>();

            SearchResult mockSearchResult = GenerateSearchResult(1);

            _searchService.SearchAsync("Patient", Arg.Any<IReadOnlyList<Tuple<string, string>>>(), CancellationToken.None).Returns(mockSearchResult);

            await _transactionBundleValidator.ValidateBundle(bundle, processingLogic, _idDictionary, CancellationToken.None);
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
