// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.GraphQl;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.GraphQl
{
    public class GraphQlHandler : IRequestHandler<GraphQlRequest, GraphQlResponse>
    {
        private readonly ISearchService _searchService;
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly IResourceDeserializer _resourceDeserializer;

        public GraphQlHandler(
            ISearchService searchService,
            IAuthorizationService<DataActions> authorizationService,
            IResourceDeserializer resourceDeserializer)
        {
            EnsureArg.IsNotNull(searchService, nameof(searchService));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(resourceDeserializer, nameof(resourceDeserializer));

            _searchService = searchService;
            _authorizationService = authorizationService;
            _resourceDeserializer = resourceDeserializer;
        }

        public async Task<GraphQlResponse> Handle(GraphQlRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.Read, cancellationToken) != DataActions.Read)
            {
                throw new UnauthorizedFhirActionException();
            }

            SearchResult result = await _searchService.SearchAsync(request.ResourceType, request.Queries, cancellationToken);

            var resultEntries = result.Results.ToList();
            var resourceElements = new List<ResourceElement>();

            foreach (SearchResultEntry entry in resultEntries)
            {
                ResourceElement element = _resourceDeserializer.Deserialize(entry.Resource);
                resourceElements.Add(element);
            }

            return new GraphQlResponse(resourceElements);
        }
    }
}
