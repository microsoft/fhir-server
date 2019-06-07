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
    /// <summary>
    /// Handler for searching resource.
    /// </summary>
    public class SearchResourceHandler : IRequestHandler<SearchResourceRequest, SearchResourceResponse>
    {
        private readonly ISearchService _searchService;
        private readonly IBundleFactory _bundleFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="SearchResourceHandler"/> class.
        /// </summary>
        /// <param name="searchService">The search service to execute the search operation.</param>
        /// <param name="bundleFactory">The bundle factory.</param>
        public SearchResourceHandler(ISearchService searchService, IBundleFactory bundleFactory)
        {
            EnsureArg.IsNotNull(searchService, nameof(searchService));
            EnsureArg.IsNotNull(bundleFactory, nameof(bundleFactory));

            _searchService = searchService;
            _bundleFactory = bundleFactory;
        }

        /// <inheritdoc />
        public async Task<SearchResourceResponse> Handle(SearchResourceRequest message, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(message, nameof(message));

            SearchResult searchResult = await _searchService.SearchAsync(message.ResourceType, message.Queries, cancellationToken);

            ResourceElement bundle = _bundleFactory.CreateSearchBundle(searchResult);

            return new SearchResourceResponse(bundle);
        }
    }
}
