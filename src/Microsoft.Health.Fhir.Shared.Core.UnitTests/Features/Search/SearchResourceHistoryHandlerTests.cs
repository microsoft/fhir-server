// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using Microsoft.Health.Fhir.Core.Features.Search;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    public class SearchResourceHistoryHandlerTests
    {
        private const string ParamNameSearchService = "searchService";
        private const string ParamNameMessage = "message";

        private readonly ISearchService _searchService = Substitute.For<ISearchService>();
        private readonly SearchResourceHistoryHandler _searchResourceHandler;

        public SearchResourceHistoryHandlerTests()
        {
            _searchResourceHandler = new SearchResourceHistoryHandler(_searchService);
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
    }
}
