// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Medino;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    /// <summary>
    /// Handles searches for current soft-deleted resources.
    /// </summary>
    public class SearchDeletedResourcesHandler : IRequestHandler<SearchDeletedResourcesRequest, SearchResourceHistoryResponse>
    {
        private readonly ISearchService _searchService;
        private readonly IBundleFactory _bundleFactory;
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly IDataResourceFilter _dataResourceFilter;

        /// <summary>
        /// Initializes a new instance of the <see cref="SearchDeletedResourcesHandler"/> class.
        /// </summary>
        public SearchDeletedResourcesHandler(
            ISearchService searchService,
            IBundleFactory bundleFactory,
            IAuthorizationService<DataActions> authorizationService,
            IDataResourceFilter dataResourceFilter)
        {
            _searchService = EnsureArg.IsNotNull(searchService, nameof(searchService));
            _bundleFactory = EnsureArg.IsNotNull(bundleFactory, nameof(bundleFactory));
            _authorizationService = EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            _dataResourceFilter = EnsureArg.IsNotNull(dataResourceFilter, nameof(dataResourceFilter));
        }

        /// <inheritdoc />
        public async Task<SearchResourceHistoryResponse> HandleAsync(SearchDeletedResourcesRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            await _authorizationService.CheckSearchAccess(cancellationToken);

            SearchResult searchResult = await _searchService.SearchDeletedAsync(
                request.ResourceType,
                request.Since,
                request.Before,
                request.Count,
                request.ContinuationToken,
                request.Sort,
                cancellationToken);

            searchResult = _dataResourceFilter.Filter(searchResult);

            ResourceElement bundle = _bundleFactory.CreateHistoryBundle(searchResult);
            return new SearchResourceHistoryResponse(bundle);
        }
    }
}
