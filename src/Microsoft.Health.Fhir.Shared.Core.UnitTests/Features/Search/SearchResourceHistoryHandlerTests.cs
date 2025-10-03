// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using Hl7.Fhir.Model;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Filters;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SearchResourceHistoryHandlerTests
    {
        private readonly ISearchService _searchService = Substitute.For<ISearchService>();
        private readonly IBundleFactory _bundleFactory = Substitute.For<IBundleFactory>();

        private readonly SearchResourceHistoryHandler _searchResourceHistoryHandler;

        public SearchResourceHistoryHandlerTests()
        {
            _searchResourceHistoryHandler = new SearchResourceHistoryHandler(
                _searchService,
                _bundleFactory,
                DisabledFhirAuthorizationService.Instance,
                new DataResourceFilter(MissingDataFilterCriteria.Default));
        }

        [Fact]
        public async Task GivenASearchResourceHistoryRequest_WhenHandled_ThenABundleShouldBeReturned()
        {
            var request = new SearchResourceHistoryRequest("Patient");

            var searchResult = new SearchResult(Enumerable.Empty<SearchResultEntry>(), null, null, new Tuple<string, string>[0]);

            _searchService.SearchHistoryAsync(request.ResourceType, null, null, null, null, null, null, null, null, CancellationToken.None).Returns(searchResult);

            var expectedBundle = new Bundle().ToResourceElement();

            _bundleFactory.CreateHistoryBundle(searchResult).Returns(expectedBundle);

            SearchResourceHistoryResponse actualResponse = await _searchResourceHistoryHandler.Handle(request, CancellationToken.None);

            Assert.NotNull(actualResponse);
            Assert.Equal(expectedBundle, actualResponse.Bundle);
        }

        [Theory]
        [InlineData(DataActions.Read)]
        [InlineData(DataActions.Search)]
        public async Task GivenASearchResourceHistoryRequest_WhenUserHasReadPermission_ThenSearchShouldSucceed(DataActions returnedDataAction)
        {
            // Arrange
            var authService = Substitute.For<IAuthorizationService<DataActions>>();
            var searchResourceHistoryHandler = new SearchResourceHistoryHandler(
                _searchService,
                _bundleFactory,
                authService,
                new DataResourceFilter(MissingDataFilterCriteria.Default));

            authService
                .CheckAccess(DataActions.Read | DataActions.Search, CancellationToken.None)
                .Returns(returnedDataAction);

            var request = new SearchResourceHistoryRequest("Patient");
            var searchResult = new SearchResult(Enumerable.Empty<SearchResultEntry>(), null, null, new Tuple<string, string>[0]);
            var expectedBundle = new Bundle().ToResourceElement();

            _searchService.SearchHistoryAsync(request.ResourceType, null, null, null, null, null, null, null, null, CancellationToken.None).Returns(searchResult);
            _bundleFactory.CreateHistoryBundle(searchResult).Returns(expectedBundle);

            // Act & Assert - Should not throw UnauthorizedFhirActionException
            var actualResponse = await searchResourceHistoryHandler.Handle(request, CancellationToken.None);

            Assert.NotNull(actualResponse);
            Assert.Equal(expectedBundle, actualResponse.Bundle);
        }

        [Theory]
        [InlineData(DataActions.None)]
        [InlineData(DataActions.Write)]
        [InlineData(DataActions.ReadById)]
        public async Task GivenASearchResourceHistoryRequest_WhenUserLacksPermissions_ThenUnauthorizedExceptionIsThrown(DataActions returnedDataAction)
        {
            // Arrange
            var authService = Substitute.For<IAuthorizationService<DataActions>>();
            var searchResourceHistoryHandler = new SearchResourceHistoryHandler(
                _searchService,
                _bundleFactory,
                authService,
                new DataResourceFilter(MissingDataFilterCriteria.Default));

            authService
                .CheckAccess(DataActions.Read | DataActions.Search, CancellationToken.None)
                .Returns(returnedDataAction);

            var request = new SearchResourceHistoryRequest("Patient");

            // Act & Assert
            await Assert.ThrowsAsync<UnauthorizedFhirActionException>(() =>
                searchResourceHistoryHandler.Handle(request, CancellationToken.None));
        }
    }
}
