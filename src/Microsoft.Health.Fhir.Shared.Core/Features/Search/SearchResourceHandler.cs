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
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly IDataResourceFilter _dataResourceFilter;

        /// <summary>
        /// Initializes a new instance of the <see cref="SearchResourceHandler"/> class.
        /// </summary>
        /// <param name="searchService">The search service to execute the search operation.</param>
        /// <param name="bundleFactory">The bundle factory.</param>
        /// <param name="authorizationService">The authorization service.</param>
        /// <param name="dataResourceFilter">The search result filter.</param>
        public SearchResourceHandler(ISearchService searchService, IBundleFactory bundleFactory, IAuthorizationService<DataActions> authorizationService, IDataResourceFilter dataResourceFilter)
        {
            EnsureArg.IsNotNull(searchService, nameof(searchService));
            EnsureArg.IsNotNull(bundleFactory, nameof(bundleFactory));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(dataResourceFilter, nameof(dataResourceFilter));

            _searchService = searchService;
            _bundleFactory = bundleFactory;
            _authorizationService = authorizationService;
            _dataResourceFilter = dataResourceFilter;
        }

        /// <inheritdoc />
        public async Task<SearchResourceResponse> HandleAsync(SearchResourceRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            // For SMART v2 compliance, search operations require the Search permission.
            // SMART v2 scopes like "patient/Patient.r" allow read-only access without search capability,
            // while "patient/Patient.s" or "patient/Patient.rs" include search permissions.
            // Users with only read permission can access resources directly by ID but cannot search.
            // We continue to allow DataActions.Read for legacy support
            var grantedAccess = await _authorizationService.CheckAccess(DataActions.Search | DataActions.Read, cancellationToken);
            if ((grantedAccess & (DataActions.Search | DataActions.Read)) == 0)
            {
                throw new UnauthorizedFhirActionException();
            }

            SearchResult searchResult = await _searchService.SearchAsync(
                resourceType: request.ResourceType,
                queryParameters: request.Queries,
                cancellationToken: cancellationToken,
                isIncludesOperation: request.IsIncludesRequest);
            searchResult = _dataResourceFilter.Filter(searchResult: searchResult);

            ResourceElement bundle = _bundleFactory.CreateSearchBundle(searchResult);

            return new SearchResourceResponse(bundle);
        }
    }
}
