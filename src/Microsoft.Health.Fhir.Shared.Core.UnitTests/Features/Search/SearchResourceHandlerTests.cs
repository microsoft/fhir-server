// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Search;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    public class SearchResourceHandlerTests
    {
        private const string ParamNameSearchService = "searchService";
        private const string ParamNameMessage = "message";

        private readonly ISearchService _searchService = Substitute.For<ISearchService>();
        private readonly SearchResourceHandler _searchResourceHandler;

        public SearchResourceHandlerTests()
        {
            _searchResourceHandler = new SearchResourceHandler(_searchService);
        }

        [Fact]
        public void GivenANullSearchService_WhenConstructorIsCalled_ThenExceptionShouldBeThrown()
        {
            Assert.Throws<ArgumentNullException>(ParamNameSearchService, () => new SearchResourceHandler(null));
        }

        [Fact]
        public async Task GivenANullMessage_WhenHandleIsCalled_ThenExceptionShouldBeThrown()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(ParamNameMessage, () => _searchResourceHandler.Handle(null, CancellationToken.None));
        }

        [Fact]
        public async Task GivenAnSearchResourceRequest_WhenHandled_ThenABundleShouldBeReturned()
        {
            SearchResourceRequest request = new SearchResourceRequest("Patient", null);

            var expectedBundle = new Bundle().ToResourceElement();

            _searchService.SearchAsync(request.ResourceType, null).Returns(expectedBundle);

            SearchResourceResponse actualResponse = await _searchResourceHandler.Handle(request, CancellationToken.None);

            Assert.NotNull(actualResponse);
            Assert.Equal(expectedBundle, actualResponse.Bundle);
        }
    }
}
