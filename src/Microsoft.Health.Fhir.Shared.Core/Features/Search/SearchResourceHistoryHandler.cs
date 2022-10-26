// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    public class SearchResourceHistoryHandler : IRequestHandler<SearchResourceHistoryRequest, SearchResourceHistoryResponse>
    {
        private readonly ISearchService _searchService;
        private readonly IBundleFactory _bundleFactory;
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly ISearchResultFilter _searchResultFilter;

        public SearchResourceHistoryHandler(ISearchService searchService, IBundleFactory bundleFactory, IAuthorizationService<DataActions> authorizationService, ISearchResultFilter searchResultFilter)
        {
            EnsureArg.IsNotNull(searchService, nameof(searchService));
            EnsureArg.IsNotNull(bundleFactory, nameof(bundleFactory));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(searchResultFilter, nameof(searchResultFilter));

            _searchService = searchService;
            _bundleFactory = bundleFactory;
            _authorizationService = authorizationService;
            _searchResultFilter = searchResultFilter;
        }

        public async Task<SearchResourceHistoryResponse> Handle(SearchResourceHistoryRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.Read, cancellationToken) != DataActions.Read)
            {
                throw new UnauthorizedFhirActionException();
            }

            SearchResult searchResult = await _searchService.SearchHistoryAsync(
                request.ResourceType,
                request.ResourceId,
                request.At,
                request.Since,
                request.Before,
                request.Count,
                request.ContinuationToken,
                request.Sort,
                cancellationToken);

            searchResult = _searchResultFilter.Filter(searchResult: searchResult);

            ResourceElement bundle = _bundleFactory.CreateHistoryBundle(searchResult);

            return new SearchResourceHistoryResponse(bundle);
        }
    }
}
