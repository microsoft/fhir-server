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
    /// <summary>
    /// Handler for searching resource.
    /// </summary>
    public class SearchResourceHandler : IRequestHandler<SearchResourceRequest, SearchResourceResponse>
    {
        private readonly ISearchService _searchService;
        private readonly IBundleFactory _bundleFactory;
        private readonly IFhirAuthorizationService _authorizationService;

        /// <summary>
        /// Initializes a new instance of the <see cref="SearchResourceHandler"/> class.
        /// </summary>
        /// <param name="searchService">The search service to execute the search operation.</param>
        /// <param name="bundleFactory">The bundle factory.</param>
        /// <param name="authorizationService">The authorization service</param>
        public SearchResourceHandler(ISearchService searchService, IBundleFactory bundleFactory, IFhirAuthorizationService authorizationService)
        {
            EnsureArg.IsNotNull(searchService, nameof(searchService));
            EnsureArg.IsNotNull(bundleFactory, nameof(bundleFactory));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));

            _searchService = searchService;
            _bundleFactory = bundleFactory;
            _authorizationService = authorizationService;
        }

        /// <inheritdoc />
        public async Task<SearchResourceResponse> Handle(SearchResourceRequest message, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(message, nameof(message));

            if (_authorizationService.CheckAccess(DataActions.Read) != DataActions.Read)
            {
                throw new UnauthorizedFhirActionException();
            }

            SearchResult searchResult = await _searchService.SearchAsync(message.ResourceType, message.Queries, cancellationToken);

            ResourceElement bundle = _bundleFactory.CreateSearchBundle(searchResult);

            return new SearchResourceResponse(bundle);
        }
    }
}
