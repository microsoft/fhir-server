// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Search;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Compartment
{
    public class SearchCompartmentHandlerTests
    {
        private readonly ISearchService _searchService = Substitute.For<ISearchService>();
        private readonly IBundleFactory _bundleFactory = Substitute.For<IBundleFactory>();

        private readonly SearchCompartmentHandler _searchCompartmentHandler;

        public SearchCompartmentHandlerTests()
        {
            _searchCompartmentHandler = new SearchCompartmentHandler(_searchService, _bundleFactory);
        }

        [Fact]
        public async Task GivenASearchCompartmentRequest_WhenHandled_ThenABundleShouldBeReturned()
        {
            var request = new SearchCompartmentRequest("Patient", "123", "Observation", new Tuple<string, string>[0]);

            var searchResult = new SearchResult(Enumerable.Empty<ResourceWrapper>(), new Tuple<string, string>[0], Array.Empty<(string parameterName, string reason)>(), null);

            _searchService.SearchCompartmentAsync(
                request.CompartmentType,
                request.CompartmentId,
                request.ResourceType,
                request.Queries,
                CancellationToken.None).Returns(searchResult);

            var expectedBundle = new Bundle().ToResourceElement();

            _bundleFactory.CreateSearchBundle(searchResult).Returns(expectedBundle);

            SearchCompartmentResponse actualResponse = await _searchCompartmentHandler.Handle(request, CancellationToken.None);

            Assert.NotNull(actualResponse);
            Assert.Equal(expectedBundle, actualResponse.Bundle);
        }
    }
}
