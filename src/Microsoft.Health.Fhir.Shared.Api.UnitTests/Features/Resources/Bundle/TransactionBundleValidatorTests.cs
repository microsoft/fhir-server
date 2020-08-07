// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Health.Fhir.Api.Features.Resources.Bundle;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Resources;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Tests.Common;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Resources.Bundle
{
    public class TransactionBundleValidatorTests
    {
        private readonly ISearchService _searchService = Substitute.For<ISearchService>();
        private TransactionBundleValidator _transactionBundleValidator;

        public TransactionBundleValidatorTests()
        {
            _transactionBundleValidator = new TransactionBundleValidator(new ResourceReferenceResolver(_searchService, new QueryStringParser()));
        }

        [Fact]
        public async Task GivenATransactionBundle_WhenContainsUniqueResources_NoExceptionShouldBeThrown()
        {
            var requestBundle = Samples.GetJsonSample("Bundle-TransactionWithValidBundleEntry");
            await _transactionBundleValidator.ValidateBundle(requestBundle.ToPoco<Hl7.Fhir.Model.Bundle>(), CancellationToken.None);
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
            var exception = await Assert.ThrowsAsync<RequestNotValidException>(() => _transactionBundleValidator.ValidateBundle(requestBundle.ToPoco<Hl7.Fhir.Model.Bundle>(), CancellationToken.None));
            Assert.Equal(expectedMessage, exception.Message);
        }

        private async Task ValidateIfBundleEntryIsUniqueAsync(Core.Models.ResourceElement requestBundle)
        {
            var resourceIdList = new HashSet<string>(StringComparer.Ordinal);
            var bundle = requestBundle.ToPoco<Hl7.Fhir.Model.Bundle>();

            var mockSearchEntry = new SearchResultEntry(
                new ResourceWrapper(
                    "123",
                    "1",
                    "Patient",
                    new RawResource("data", Core.Models.FhirResourceFormat.Json),
                    null,
                    DateTimeOffset.MinValue,
                    false,
                    null,
                    null,
                    null));

            var mockSearchResult = new SearchResult(new[] { mockSearchEntry }, new Tuple<string, string>[0], Array.Empty<(string parameterName, string reason)>(), null);

            _searchService.SearchAsync("Patient", Arg.Any<IReadOnlyList<Tuple<string, string>>>(), CancellationToken.None).Returns(mockSearchResult);

            await _transactionBundleValidator.ValidateBundle(bundle, CancellationToken.None);
        }
    }
}
