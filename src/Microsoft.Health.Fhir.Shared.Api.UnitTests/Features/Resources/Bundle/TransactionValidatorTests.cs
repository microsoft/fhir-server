// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Health.Fhir.Api.Features.Resources.Bundle;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Resources.Bundle
{
    public class TransactionValidatorTests
    {
        private readonly ISearchService _searchService = Substitute.For<ISearchService>();
        private TransactionValidator _transactionValidator;

        public TransactionValidatorTests()
        {
            _transactionValidator = new TransactionValidator(_searchService);
        }

        [Theory]
        [InlineData("Bundle-Transaction")]
        [InlineData("Bundle-TransactionWithPOSTFullUrlMatchesWithPUTRequestUrl")]
        public async System.Threading.Tasks.Task GivenABundleWithUniqueResources_TransactionValidatorShouldNotThrowExceptionAsync(string inputBundle)
        {
            var requestBundle = Samples.GetJsonSample(inputBundle);
            await _transactionValidator.ValidateBundle(requestBundle.ToPoco<Hl7.Fhir.Model.Bundle>());
        }

        [Theory]
        [InlineData("Bundle-TransactionWithConditionalReferenceReferringToSameResource")]
        [InlineData("Bundle-TransactionWithMultipleEntriesModifyingSameResource")]
        public async System.Threading.Tasks.Task GivenATransactionBundle_IfContainsMultipleEntriesWithTheSameResource_TransactionValidatorShouldThrowException(string inputBundle)
        {
            var expectedMessage = "Bundle contains multiple resources that refers to the same resource 'Patient/123'.";

            var requestBundle = Samples.GetJsonSample(inputBundle);
            var exception = await Assert.ThrowsAsync<RequestNotValidException>(() => ValidateIfBundleEntryIsUniqueAsync(requestBundle));
            Assert.Equal(expectedMessage, exception.Message);
        }

        private async System.Threading.Tasks.Task ValidateIfBundleEntryIsUniqueAsync(Core.Models.ResourceElement requestBundle)
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

            var searchResult = new SearchResult(new[] { mockSearchEntry }, new Tuple<string, string>[0], Array.Empty<(string parameterName, string reason)>(), null);

            _searchService.SearchAsync("Patient", Arg.Any<IReadOnlyList<Tuple<string, string>>>(), CancellationToken.None).Returns(searchResult);
            await _transactionValidator.ValidateBundle(bundle);
        }
    }
}
