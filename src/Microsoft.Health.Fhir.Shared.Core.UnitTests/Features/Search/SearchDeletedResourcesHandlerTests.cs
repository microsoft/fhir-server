// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using Hl7.Fhir.Model;
using Microsoft.Health.Core.Features.Security.Authorization;
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
    public class SearchDeletedResourcesHandlerTests
    {
        [Fact]
        public async Task GivenADeletedResourceSearch_WhenHandled_ThenAHistoryBundleIsReturned()
        {
            ISearchService searchService = Substitute.For<ISearchService>();
            IBundleFactory bundleFactory = Substitute.For<IBundleFactory>();
            var handler = new SearchDeletedResourcesHandler(
                searchService,
                bundleFactory,
                DisabledFhirAuthorizationService.Instance,
                new DataResourceFilter(MissingDataFilterCriteria.Default));
            var request = new SearchDeletedResourcesRequest("Patient", null, null, null, null, null);
            var searchResult = new SearchResult(Enumerable.Empty<SearchResultEntry>(), null, null, Array.Empty<Tuple<string, string>>());
            var expectedBundle = new Bundle().ToResourceElement();

            searchService.SearchDeletedAsync("Patient", null, null, null, null, null, CancellationToken.None).Returns(searchResult);
            bundleFactory.CreateHistoryBundle(searchResult).Returns(expectedBundle);

            SearchResourceHistoryResponse response = await handler.HandleAsync(request, CancellationToken.None);

            Assert.Same(expectedBundle, response.Bundle);
        }

        [Theory]
        [InlineData(DataActions.None)]
        [InlineData(DataActions.Write)]
        [InlineData(DataActions.ReadById)]
        public async Task GivenADeletedResourceSearch_WhenUserLacksSearchAccess_ThenAuthorizationFails(DataActions dataActions)
        {
            ISearchService searchService = Substitute.For<ISearchService>();
            IBundleFactory bundleFactory = Substitute.For<IBundleFactory>();
            IAuthorizationService<DataActions> authorizationService = Substitute.For<IAuthorizationService<DataActions>>();
            var handler = new SearchDeletedResourcesHandler(
                searchService,
                bundleFactory,
                authorizationService,
                new DataResourceFilter(MissingDataFilterCriteria.Default));
            var request = new SearchDeletedResourcesRequest(null, null, null, null, null, null);

            authorizationService.CheckAccess(DataActions.Read | DataActions.Search, CancellationToken.None).Returns(dataActions);

            await Assert.ThrowsAsync<Microsoft.Health.Fhir.Core.Exceptions.UnauthorizedFhirActionException>(
                () => handler.HandleAsync(request, CancellationToken.None));
        }
    }
}
