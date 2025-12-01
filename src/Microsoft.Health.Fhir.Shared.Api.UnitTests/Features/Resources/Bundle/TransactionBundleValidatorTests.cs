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
        private readonly ILogger<TransactionBundleValidator> _logger = Substitute.For<ILogger<TransactionBundleValidator>>();
        private readonly ILogger<ResourceReferenceResolver> _loggerResourceReferenceResolver = Substitute.For<ILogger<ResourceReferenceResolver>>();
        private readonly TransactionBundleValidator _transactionBundleValidator;
        private readonly Dictionary<string, (string resourceId, string resourceType)> _idDictionary;

        public TransactionBundleValidatorTests()
        {
            _transactionBundleValidator = new TransactionBundleValidator(new ResourceReferenceResolver(_searchService, new QueryStringParser(), _loggerResourceReferenceResolver), _logger);
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

        [Theory]
        [InlineData("Patient?")]
        [InlineData("")]
        [InlineData("?test")]
        public async Task GivenATransactionBundle_WhenUrlIsNotWellFormed_ThenRequestNotValidExceptionShouldBeThrown(string invalidUrl)
        {
            var bundle = Samples.GetBasicTransactionBundleWithSingleResource();

            bundle.Entry.First().Request = new Hl7.Fhir.Model.Bundle.RequestComponent()
            {
                Method = Hl7.Fhir.Model.Bundle.HTTPVerb.PUT,
                Url = invalidUrl,
            };

            await Assert.ThrowsAsync<RequestNotValidException>(() => _transactionBundleValidator.ValidateBundle(bundle, _idDictionary, CancellationToken.None));
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

        [Fact]
        public async Task GivenATransactionBundle_WithCircularReferences_ThenValidationSucceeds()
        {
            // Test circular reference handling: Patient -> Observation -> Patient
            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Type = Hl7.Fhir.Model.Bundle.BundleType.Transaction,
                Entry = new List<Hl7.Fhir.Model.Bundle.EntryComponent>
                {
                    new Hl7.Fhir.Model.Bundle.EntryComponent
                    {
                        FullUrl = "urn:uuid:patient-1",
                        Resource = new Hl7.Fhir.Model.Patient
                        {
                            Id = string.Empty,
                            ManagingOrganization = new Hl7.Fhir.Model.ResourceReference { Reference = "urn:uuid:org-1" },
                        },
                        Request = new Hl7.Fhir.Model.Bundle.RequestComponent
                        {
                            Method = Hl7.Fhir.Model.Bundle.HTTPVerb.POST,
                            Url = "Patient",
                        },
                    },
                    new Hl7.Fhir.Model.Bundle.EntryComponent
                    {
                        FullUrl = "urn:uuid:org-1",
                        Resource = new Hl7.Fhir.Model.Organization
                        {
                            Id = string.Empty,
                            PartOf = new Hl7.Fhir.Model.ResourceReference { Reference = "urn:uuid:patient-1" },
                        },
                        Request = new Hl7.Fhir.Model.Bundle.RequestComponent
                        {
                            Method = Hl7.Fhir.Model.Bundle.HTTPVerb.POST,
                            Url = "Organization",
                        },
                    },
                },
            };

            // Circular references are allowed in bundles - they should not throw
            await _transactionBundleValidator.ValidateBundle(bundle, _idDictionary, CancellationToken.None);

            // Should have both entries in the dictionary
            Assert.Equal(2, _idDictionary.Count);
        }

        [Fact]
        public async Task GivenATransactionBundle_WithMultipleReferencesToSameResource_ThenValidationSucceeds()
        {
            // Test multiple references to the same resource
            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Type = Hl7.Fhir.Model.Bundle.BundleType.Transaction,
                Entry = new List<Hl7.Fhir.Model.Bundle.EntryComponent>
                {
                    new Hl7.Fhir.Model.Bundle.EntryComponent
                    {
                        FullUrl = "urn:uuid:patient-1",
                        Resource = new Hl7.Fhir.Model.Patient { Id = string.Empty },
                        Request = new Hl7.Fhir.Model.Bundle.RequestComponent
                        {
                            Method = Hl7.Fhir.Model.Bundle.HTTPVerb.POST,
                            Url = "Patient",
                        },
                    },
                    new Hl7.Fhir.Model.Bundle.EntryComponent
                    {
                        Resource = new Hl7.Fhir.Model.Observation
                        {
                            Status = Hl7.Fhir.Model.ObservationStatus.Final,
                            Code = new Hl7.Fhir.Model.CodeableConcept(),
                            Subject = new Hl7.Fhir.Model.ResourceReference { Reference = "urn:uuid:patient-1" },
                            Performer = new List<Hl7.Fhir.Model.ResourceReference>
                            {
                                new Hl7.Fhir.Model.ResourceReference { Reference = "urn:uuid:patient-1" },
                                new Hl7.Fhir.Model.ResourceReference { Reference = "urn:uuid:patient-1" },
                            },
                        },
                        Request = new Hl7.Fhir.Model.Bundle.RequestComponent
                        {
                            Method = Hl7.Fhir.Model.Bundle.HTTPVerb.POST,
                            Url = "Observation",
                        },
                    },
                },
            };

            // Multiple references to the same resource should be allowed
            await _transactionBundleValidator.ValidateBundle(bundle, _idDictionary, CancellationToken.None);

            Assert.Single(_idDictionary);
        }

        [Fact]
        public async Task GivenATransactionBundle_WithMixedAbsoluteAndRelativeReferences_ThenValidationSucceeds()
        {
            // Test mixed reference types
            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Type = Hl7.Fhir.Model.Bundle.BundleType.Transaction,
                Entry = new List<Hl7.Fhir.Model.Bundle.EntryComponent>
                {
                    new Hl7.Fhir.Model.Bundle.EntryComponent
                    {
                        FullUrl = "urn:uuid:patient-1",
                        Resource = new Hl7.Fhir.Model.Patient { Id = string.Empty },
                        Request = new Hl7.Fhir.Model.Bundle.RequestComponent
                        {
                            Method = Hl7.Fhir.Model.Bundle.HTTPVerb.POST,
                            Url = "Patient",
                        },
                    },
                    new Hl7.Fhir.Model.Bundle.EntryComponent
                    {
                        Resource = new Hl7.Fhir.Model.Observation
                        {
                            Status = Hl7.Fhir.Model.ObservationStatus.Final,
                            Code = new Hl7.Fhir.Model.CodeableConcept(),
                            Subject = new Hl7.Fhir.Model.ResourceReference { Reference = "urn:uuid:patient-1" },
                            BasedOn = new List<Hl7.Fhir.Model.ResourceReference>
                            {
                                new Hl7.Fhir.Model.ResourceReference { Reference = "Patient/external-123" },
                            },
                        },
                        Request = new Hl7.Fhir.Model.Bundle.RequestComponent
                        {
                            Method = Hl7.Fhir.Model.Bundle.HTTPVerb.POST,
                            Url = "Observation",
                        },
                    },
                },
            };

            await _transactionBundleValidator.ValidateBundle(bundle, _idDictionary, CancellationToken.None);

            Assert.Single(_idDictionary);
        }

        [Fact]
        public async Task GivenATransactionBundle_WithConditionalUpdateAndConditionalReference_ThenValidationSucceeds()
        {
            // Test conditional update with conditional reference
            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Type = Hl7.Fhir.Model.Bundle.BundleType.Transaction,
                Entry = new List<Hl7.Fhir.Model.Bundle.EntryComponent>
                {
                    new Hl7.Fhir.Model.Bundle.EntryComponent
                    {
                        Resource = new Hl7.Fhir.Model.Patient
                        {
                            Identifier = new List<Hl7.Fhir.Model.Identifier>
                            {
                                new Hl7.Fhir.Model.Identifier("http://example.org", "patient-123"),
                            },
                        },
                        Request = new Hl7.Fhir.Model.Bundle.RequestComponent
                        {
                            Method = Hl7.Fhir.Model.Bundle.HTTPVerb.PUT,
                            Url = "Patient?identifier=patient-123",
                        },
                    },
                    new Hl7.Fhir.Model.Bundle.EntryComponent
                    {
                        Resource = new Hl7.Fhir.Model.Observation
                        {
                            Status = Hl7.Fhir.Model.ObservationStatus.Final,
                            Code = new Hl7.Fhir.Model.CodeableConcept(),
                            Subject = new Hl7.Fhir.Model.ResourceReference { Reference = "Patient?identifier=patient-123" },
                        },
                        Request = new Hl7.Fhir.Model.Bundle.RequestComponent
                        {
                            Method = Hl7.Fhir.Model.Bundle.HTTPVerb.POST,
                            Url = "Observation",
                        },
                    },
                },
            };

            MockSearchAsync(1);

            await _transactionBundleValidator.ValidateBundle(bundle, _idDictionary, CancellationToken.None);

            // Both entries should be in the dictionary
            Assert.Equal(2, _idDictionary.Count);
        }

        [Fact]
        public async Task GivenATransactionBundle_WithDependentCreatesInWrongOrder_ThenValidationSucceeds()
        {
            // Test that validation allows dependent creates even if out of order
            // (execution order is handled by the handler, not validator)
            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Type = Hl7.Fhir.Model.Bundle.BundleType.Transaction,
                Entry = new List<Hl7.Fhir.Model.Bundle.EntryComponent>
                {
                    new Hl7.Fhir.Model.Bundle.EntryComponent
                    {
                        Resource = new Hl7.Fhir.Model.Observation
                        {
                            Status = Hl7.Fhir.Model.ObservationStatus.Final,
                            Code = new Hl7.Fhir.Model.CodeableConcept(),
                            Subject = new Hl7.Fhir.Model.ResourceReference { Reference = "urn:uuid:patient-1" },
                        },
                        Request = new Hl7.Fhir.Model.Bundle.RequestComponent
                        {
                            Method = Hl7.Fhir.Model.Bundle.HTTPVerb.POST,
                            Url = "Observation",
                        },
                    },
                    new Hl7.Fhir.Model.Bundle.EntryComponent
                    {
                        FullUrl = "urn:uuid:patient-1",
                        Resource = new Hl7.Fhir.Model.Patient { Id = string.Empty },
                        Request = new Hl7.Fhir.Model.Bundle.RequestComponent
                        {
                            Method = Hl7.Fhir.Model.Bundle.HTTPVerb.POST,
                            Url = "Patient",
                        },
                    },
                },
            };

            // Validation should succeed - execution order is handled elsewhere
            await _transactionBundleValidator.ValidateBundle(bundle, _idDictionary, CancellationToken.None);

            Assert.Single(_idDictionary);
        }

        [Fact]
        public async Task GivenATransactionBundle_WithConditionalCreateReturningNoMatches_ThenNewEntryAddedToIdDictionary()
        {
            // Test conditional create that matches no existing resources
            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Type = Hl7.Fhir.Model.Bundle.BundleType.Transaction,
                Entry = new List<Hl7.Fhir.Model.Bundle.EntryComponent>
                {
                    new Hl7.Fhir.Model.Bundle.EntryComponent
                    {
                        FullUrl = "urn:uuid:new-patient",
                        Resource = new Hl7.Fhir.Model.Patient
                        {
                            Identifier = new List<Hl7.Fhir.Model.Identifier>
                            {
                                new Hl7.Fhir.Model.Identifier("http://example.org", "new-patient-id"),
                            },
                        },
                        Request = new Hl7.Fhir.Model.Bundle.RequestComponent
                        {
                            Method = Hl7.Fhir.Model.Bundle.HTTPVerb.POST,
                            Url = "Patient",
                            IfNoneExist = "identifier=new-patient-id",
                        },
                    },
                },
            };

            // Mock search to return empty result
            var emptySearchResult = new SearchResult(
                Enumerable.Empty<SearchResultEntry>(),
                null,
                null,
                Array.Empty<Tuple<string, string>>());

            _searchService.SearchAsync(
                    Arg.Any<string>(),
                    Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                    Arg.Any<CancellationToken>())
                .Returns(emptySearchResult);

            await _transactionBundleValidator.ValidateBundle(bundle, _idDictionary, CancellationToken.None);

            // New resource should be added to dictionary
            Assert.Single(_idDictionary);
            Assert.Contains("urn:uuid:new-patient", _idDictionary.Keys);
        }

        [Fact]
        public async Task GivenATransactionBundle_WithEmptyEntryList_ThenValidationSucceeds()
        {
            // Test empty bundle
            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Type = Hl7.Fhir.Model.Bundle.BundleType.Transaction,
                Entry = new List<Hl7.Fhir.Model.Bundle.EntryComponent>(),
            };

            await _transactionBundleValidator.ValidateBundle(bundle, _idDictionary, CancellationToken.None);

            Assert.Empty(_idDictionary);
        }

        [Fact]
        public async Task GivenATransactionBundle_WithGETRequestAndFullUrl_ThenValidationSucceeds()
        {
            // Test GET request in transaction bundle
            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Type = Hl7.Fhir.Model.Bundle.BundleType.Transaction,
                Entry = new List<Hl7.Fhir.Model.Bundle.EntryComponent>
                {
                    new Hl7.Fhir.Model.Bundle.EntryComponent
                    {
                        Request = new Hl7.Fhir.Model.Bundle.RequestComponent
                        {
                            Method = Hl7.Fhir.Model.Bundle.HTTPVerb.GET,
                            Url = "Patient/123",
                        },
                    },
                },
            };

            await _transactionBundleValidator.ValidateBundle(bundle, _idDictionary, CancellationToken.None);

            Assert.Empty(_idDictionary);
        }

        [Fact]
        public async Task GivenATransactionBundle_WithPUTToExistingResourceIdAndReferences_ThenValidationSucceeds()
        {
            // Test PUT to specific resource ID
            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Type = Hl7.Fhir.Model.Bundle.BundleType.Transaction,
                Entry = new List<Hl7.Fhir.Model.Bundle.EntryComponent>
                {
                    new Hl7.Fhir.Model.Bundle.EntryComponent
                    {
                        FullUrl = "urn:uuid:update-patient",
                        Resource = new Hl7.Fhir.Model.Patient { Id = "existing-123" },
                        Request = new Hl7.Fhir.Model.Bundle.RequestComponent
                        {
                            Method = Hl7.Fhir.Model.Bundle.HTTPVerb.PUT,
                            Url = "Patient/existing-123",
                        },
                    },
                    new Hl7.Fhir.Model.Bundle.EntryComponent
                    {
                        Resource = new Hl7.Fhir.Model.Observation
                        {
                            Status = Hl7.Fhir.Model.ObservationStatus.Final,
                            Code = new Hl7.Fhir.Model.CodeableConcept(),
                            Subject = new Hl7.Fhir.Model.ResourceReference { Reference = "urn:uuid:update-patient" },
                        },
                        Request = new Hl7.Fhir.Model.Bundle.RequestComponent
                        {
                            Method = Hl7.Fhir.Model.Bundle.HTTPVerb.POST,
                            Url = "Observation",
                        },
                    },
                },
            };

            await _transactionBundleValidator.ValidateBundle(bundle, _idDictionary, CancellationToken.None);

            // PUT with fullUrl should be in dictionary for reference resolution
            Assert.Single(_idDictionary);
            Assert.Equal("existing-123", _idDictionary["urn:uuid:update-patient"].resourceId);
            Assert.Equal("Patient", _idDictionary["urn:uuid:update-patient"].resourceType);
        }

        [Fact]
        public async Task GivenATransactionBundle_WithContainedResourceReferences_ThenValidationSucceeds()
        {
            // Test contained resource references (should not be validated as external references)
            var containedPatient = new Hl7.Fhir.Model.Patient
            {
                Id = "contained-patient-1",
            };

            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Type = Hl7.Fhir.Model.Bundle.BundleType.Transaction,
                Entry = new List<Hl7.Fhir.Model.Bundle.EntryComponent>
                {
                    new Hl7.Fhir.Model.Bundle.EntryComponent
                    {
                        Resource = new Hl7.Fhir.Model.Observation
                        {
                            Status = Hl7.Fhir.Model.ObservationStatus.Final,
                            Code = new Hl7.Fhir.Model.CodeableConcept(),
                            Contained = new List<Hl7.Fhir.Model.Resource> { containedPatient },
                            Subject = new Hl7.Fhir.Model.ResourceReference { Reference = "#contained-patient-1" },
                        },
                        Request = new Hl7.Fhir.Model.Bundle.RequestComponent
                        {
                            Method = Hl7.Fhir.Model.Bundle.HTTPVerb.POST,
                            Url = "Observation",
                        },
                    },
                },
            };

            await _transactionBundleValidator.ValidateBundle(bundle, _idDictionary, CancellationToken.None);

            // Contained references should not require dictionary entries
            Assert.Empty(_idDictionary);
        }
    }
}
