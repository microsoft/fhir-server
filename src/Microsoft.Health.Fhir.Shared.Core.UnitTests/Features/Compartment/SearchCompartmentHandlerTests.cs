﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Compartment
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SearchCompartmentHandlerTests
    {
        private readonly ISearchService _searchService = Substitute.For<ISearchService>();
        private readonly IBundleFactory _bundleFactory = Substitute.For<IBundleFactory>();

        private readonly SearchCompartmentHandler _searchCompartmentHandler;

        public SearchCompartmentHandlerTests()
        {
            _searchCompartmentHandler = new SearchCompartmentHandler(_searchService, _bundleFactory, DisabledFhirAuthorizationService.Instance);
        }

        [Fact]
        public async Task GivenASearchCompartmentRequest_WhenHandled_ThenABundleShouldBeReturned()
        {
            var request = new SearchCompartmentRequest("Patient", "123", "Observation", new Tuple<string, string>[0]);

            var searchResult = new SearchResult(Enumerable.Empty<SearchResultEntry>(), null, null, new Tuple<string, string>[0]);

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
