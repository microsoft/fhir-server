// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Health.Fhir.Core.Features.GraphQl;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.GraphQl;
using Microsoft.Health.Fhir.Core.Models;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.GraphQl
{
    public class GraphQlHandlerTests
    {
        private readonly ISearchService _searchService = Substitute.For<ISearchService>();
        private readonly IResourceDeserializer _resourceDeserializer = Substitute.For<IResourceDeserializer>();

        private readonly GraphQlHandler _graphQlHandler;

        public GraphQlHandlerTests()
        {
            _graphQlHandler = new GraphQlHandler(_searchService, DisabledFhirAuthorizationService.Instance, _resourceDeserializer);
        }

        [Fact]
        public async Task GivenAGraphQlRequest_WhenHandled_ThenAListOfResourceElementsShouldBeReturned()
        {
            var request = new GraphQlRequest("Patient", null);

            var searchResult = new SearchResult(Enumerable.Empty<SearchResultEntry>(), null, null, new Tuple<string, string>[0]);

            _searchService.SearchAsync(request.ResourceType, request.Queries, CancellationToken.None).Returns(searchResult);

            var resultEntries = searchResult.Results.ToList();
            var expectedResourceElements = new List<ResourceElement>();

            foreach (SearchResultEntry entry in resultEntries)
            {
                ResourceElement element = _resourceDeserializer.Deserialize(entry.Resource);
                expectedResourceElements.Add(element);
            }

            GraphQlResponse actualResponse = await _graphQlHandler.Handle(request, CancellationToken.None);

            Assert.NotNull(actualResponse);
            Assert.Equal(expectedResourceElements, actualResponse.ResourceElements);
        }
    }
}
