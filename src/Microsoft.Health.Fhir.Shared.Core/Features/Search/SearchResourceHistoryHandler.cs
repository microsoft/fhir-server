// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    public class SearchResourceHistoryHandler : IRequestHandler<SearchResourceHistoryRequest, SearchResourceHistoryResponse>
    {
        private readonly ISearchService _searchService;
        private readonly IBundleFactory _bundleFactory;
        private readonly IFhirAuthorizationService _authorizationService;

        public SearchResourceHistoryHandler(ISearchService searchService, IBundleFactory bundleFactory, IFhirAuthorizationService authorizationService)
        {
            EnsureArg.IsNotNull(searchService, nameof(searchService));
            EnsureArg.IsNotNull(bundleFactory, nameof(bundleFactory));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));

            _searchService = searchService;
            _bundleFactory = bundleFactory;
            _authorizationService = authorizationService;
        }

        public async Task<SearchResourceHistoryResponse> Handle(SearchResourceHistoryRequest message, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(message, nameof(message));

            if (_authorizationService.CheckAccess(DataActions.Read) != DataActions.Read)
            {
                throw new UnauthorizedFhirActionException();
            }

            SearchResult searchResult = await _searchService.SearchHistoryAsync(
                message.ResourceType,
                message.ResourceId,
                message.At,
                message.Since,
                message.Before,
                message.Count,
                message.ContinuationToken,
                cancellationToken);

            ResourceElement bundle = _bundleFactory.CreateHistoryBundle(searchResult);

            return new SearchResourceHistoryResponse(bundle);
        }
    }
}
