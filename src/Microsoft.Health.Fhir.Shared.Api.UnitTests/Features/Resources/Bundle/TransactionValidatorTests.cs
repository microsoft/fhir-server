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
using Microsoft.Health.Fhir.Core.Models;
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

            var queries = new List<Tuple<string, string>>();
            string parameterKey = "identifier";
            string parameterValue = "http:/example.org/fhir/ids|234259";
            queries.Add(Tuple.Create(parameterKey, parameterValue));

            var searchResult = new SearchResult(new[] { mockSearchEntry }, new Tuple<string, string>[0], Array.Empty<(string parameterName, string reason)>(), null);
            _searchService.SearchAsync(KnownResourceTypes.Patient, queries, CancellationToken.None).Returns(searchResult);
            _transactionValidator = new TransactionValidator(_searchService);
        }

        [Theory]
        [InlineData("Bundle-Transaction")]
        [InlineData("Bundle-TransactionWithPOSTFullUrlMatchesWithPUTRequestUrl")]
        public async System.Threading.Tasks.Task GivenABundleWithUniqueResources_TransactionValidatorShouldNotThrowExceptionAsync(string inputBundle)
        {
            var requestBundle = Samples.GetJsonSample(inputBundle);
            await _transactionValidator.ValidateTransactionBundle(requestBundle.ToPoco<Hl7.Fhir.Model.Bundle>());
        }

        [Theory]
        [InlineData("Bundle-TransactionWithConditionalReferenceReferringToSameResource")]
        [InlineData("Bundle-TransactionWithMultipleEntriesModifyingSameResource")]
        public void GivenATransactionBundle_IfContainsMultipleEntriesWithTheSameResource_TransactionValidatorShouldThrowException(string inputBundle)
        {
            var expectedMessage = "Bundle contains multiple resources that refers to the same resource 'Patient/123'.";

            var requestBundle = Samples.GetJsonSample(inputBundle);
            var exception = Assert.Throws<RequestNotValidException>(() => ValidateIfBundleEntryIsUniqueAsync(requestBundle));
            Assert.Equal(expectedMessage, exception.Message);
        }

        private void ValidateIfBundleEntryIsUniqueAsync(Core.Models.ResourceElement requestBundle)
        {
            var resourceIdList = new HashSet<string>(StringComparer.Ordinal);
            var bundle = requestBundle.ToPoco<Hl7.Fhir.Model.Bundle>();

            foreach (var entry in bundle.Entry)
            {
                string mockResourceID = "Patient/123";
                TransactionValidator.CheckIfMultipleResourceExists(resourceIdList, mockResourceID);
            }
        }
    }
}
