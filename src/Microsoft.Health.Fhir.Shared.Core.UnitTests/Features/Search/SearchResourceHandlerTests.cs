// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Filters;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SearchResourceHandlerTests
    {
        private readonly ISearchService _searchService = Substitute.For<ISearchService>();
        private readonly IBundleFactory _bundleFactory = Substitute.For<IBundleFactory>();

        private readonly SearchResourceHandler _searchResourceHandler;

        public SearchResourceHandlerTests()
        {
            _searchResourceHandler = new SearchResourceHandler(
                _searchService,
                _bundleFactory,
                DisabledFhirAuthorizationService.Instance,
                new DataResourceFilter(MissingDataFilterCriteria.Default));
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

        [Fact]
        public async Task GivenASearchResourceRequest_WhenUserHasSearchPermission_ThenSearchSucceeds()
        {
            var authorizationService = Substitute.For<IAuthorizationService<DataActions>>();
            var searchResourceHandler = new SearchResourceHandler(
                _searchService,
                _bundleFactory,
                authorizationService,
                new DataResourceFilter(MissingDataFilterCriteria.Default));

            var request = new SearchResourceRequest("Patient", null);
            var searchResult = new SearchResult(Enumerable.Empty<SearchResultEntry>(), null, null, new Tuple<string, string>[0]);
            var expectedBundle = new Bundle().ToResourceElement();

            // Setup authorization to return Search permission
            authorizationService.CheckAccess(Arg.Any<DataActions>(), CancellationToken.None)
                .Returns(DataActions.Search);

            _searchService.SearchAsync(request.ResourceType, request.Queries, CancellationToken.None).Returns(searchResult);
            _bundleFactory.CreateSearchBundle(searchResult).Returns(expectedBundle);

            SearchResourceResponse actualResponse = await searchResourceHandler.Handle(request, CancellationToken.None);

            Assert.NotNull(actualResponse);
            Assert.Equal(expectedBundle, actualResponse.Bundle);
        }

        [Fact]
        public async Task GivenASearchResourceRequest_WhenUserHasOnlyReadPermission_ThenUnauthorizedExceptionThrown()
        {
            var authorizationService = Substitute.For<IAuthorizationService<DataActions>>();

            // Setup authorization to return only Read permission (no Search permission)
            // This simulates SMART v2 scope like "patient/Patient.r" which only allows direct access
            authorizationService.CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>())
                .Returns(DataActions.ReadById);

            var searchResourceHandler = new SearchResourceHandler(
                _searchService,
                _bundleFactory,
                authorizationService,
                new DataResourceFilter(MissingDataFilterCriteria.Default));

            var request = new SearchResourceRequest("Patient", null);

            await Assert.ThrowsAsync<UnauthorizedFhirActionException>(() =>
                searchResourceHandler.Handle(request, CancellationToken.None));
        }

        [Fact]
        public async Task GivenASearchResourceRequest_WhenUserHasReadAndSearchPermissions_ThenSearchSucceeds()
        {
            var authorizationService = Substitute.For<IAuthorizationService<DataActions>>();
            var searchResourceHandler = new SearchResourceHandler(
                _searchService,
                _bundleFactory,
                authorizationService,
                new DataResourceFilter(MissingDataFilterCriteria.Default));

            var request = new SearchResourceRequest("Patient", null);
            var searchResult = new SearchResult(Enumerable.Empty<SearchResultEntry>(), null, null, new Tuple<string, string>[0]);
            var expectedBundle = new Bundle().ToResourceElement();

            // Setup authorization to return Search permission (which is what we check for)
            // This simulates SMART v1 ".read" or v2 ".rs" scopes
            authorizationService.CheckAccess(Arg.Any<DataActions>(), CancellationToken.None)
                .Returns(DataActions.Search | DataActions.Read);

            _searchService.SearchAsync(request.ResourceType, request.Queries, CancellationToken.None).Returns(searchResult);
            _bundleFactory.CreateSearchBundle(searchResult).Returns(expectedBundle);

            SearchResourceResponse actualResponse = await searchResourceHandler.Handle(request, CancellationToken.None);

            Assert.NotNull(actualResponse);
            Assert.Equal(expectedBundle, actualResponse.Bundle);
        }

        [Fact]
        public async Task GivenASearchResourceRequest_WhenUserHasNoPermissions_ThenUnauthorizedExceptionThrown()
        {
            var authorizationService = Substitute.For<IAuthorizationService<DataActions>>();
            var searchResourceHandler = new SearchResourceHandler(
                _searchService,
                _bundleFactory,
                authorizationService,
                new DataResourceFilter(MissingDataFilterCriteria.Default));

            var request = new SearchResourceRequest("Patient", null);

            // Setup authorization to return no permissions
            authorizationService.CheckAccess(Arg.Any<DataActions>(), CancellationToken.None)
                .Returns(DataActions.None);

            await Assert.ThrowsAsync<UnauthorizedFhirActionException>(() =>
                searchResourceHandler.Handle(request, CancellationToken.None));
        }
    }
}
