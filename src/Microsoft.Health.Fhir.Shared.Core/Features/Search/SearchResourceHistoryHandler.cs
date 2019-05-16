// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    public class SearchResourceHistoryHandler : IRequestHandler<SearchResourceHistoryRequest, SearchResourceHistoryResponse>
    {
        private readonly ISearchService _searchService;
        private readonly IBundleFactory _bundleFactory;

        public SearchResourceHistoryHandler(ISearchService searchService, IBundleFactory bundleFactory)
        {
            EnsureArg.IsNotNull(searchService, nameof(searchService));
            EnsureArg.IsNotNull(bundleFactory, nameof(bundleFactory));

            _searchService = searchService;
            _bundleFactory = bundleFactory;
        }

        public async Task<SearchResourceHistoryResponse> Handle(SearchResourceHistoryRequest message, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(message, nameof(message));

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
