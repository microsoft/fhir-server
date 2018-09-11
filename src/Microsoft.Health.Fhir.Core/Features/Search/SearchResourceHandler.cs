// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.Health.Fhir.Core.Messages.Search;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    /// <summary>
    /// Handler for searching resource.
    /// </summary>
    public class SearchResourceHandler : IRequestHandler<SearchResourceRequest, SearchResourceResponse>
    {
        private readonly ISearchService _searchService;

        /// <summary>
        /// Initializes a new instance of the <see cref="SearchResourceHandler"/> class.
        /// </summary>
        /// <param name="searchService">The search service to execute the search operation.</param>
        public SearchResourceHandler(ISearchService searchService)
        {
            EnsureArg.IsNotNull(searchService, nameof(searchService));

            _searchService = searchService;
        }

        /// <inheritdoc />
        public async Task<SearchResourceResponse> Handle(SearchResourceRequest message, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(message, nameof(message));

            Bundle bundle = await _searchService.SearchAsync(message.ResourceType, message.Queries, cancellationToken);

            Debug.Assert(bundle != null, "SearchService should not return null bundle.");

            return new SearchResourceResponse(bundle);
        }
    }
}
