// -------------------------------------------------------------------------------------------------
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

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SearchResourceHandlerTests
    {
        private readonly ISearchService _searchService = Substitute.For<ISearchService>();
        private readonly IBundleFactory _bundleFactory = Substitute.For<IBundleFactory>();

        private readonly SearchResourceHandler _searchResourceHandler;

        public SearchResourceHandlerTests()
        {
            _searchResourceHandler = new SearchResourceHandler(_searchService, _bundleFactory, DisabledFhirAuthorizationService.Instance);
        }

        [Fact]
        public async Task GivenASearchResourceRequest_WhenHandled_ThenABundleShouldBeReturned()
        {
            var request = new SearchResourceRequest("Patient", null);

            var searchResult = new SearchResult(Enumerable.Empty<SearchResultEntry>(), null, null, new Tuple<string, string>[0]);

            _searchService.SearchAsync(request.ResourceType, request.Queries, CancellationToken.None).Returns(searchResult);

            var expectedBundle = new Bundle().ToResourceElement();

            _bundleFactory.CreateSearchBundle(searchResult).Returns(expectedBundle);

            SearchResourceResponse actualResponse = await _searchResourceHandler.Handle(request, CancellationToken.None);

            Assert.NotNull(actualResponse);
            Assert.Equal(expectedBundle, actualResponse.Bundle);
        }
    }
}
