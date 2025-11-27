// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Medino;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages;

namespace Microsoft.Health.Fhir.Core.Features.Resources
{
    public abstract class ConditionalResourceHandler<TRequest, TResponse> : BaseResourceHandler, IRequestHandler<TRequest, TResponse>
        where TRequest : ConditionalResourceRequest<TResponse>
    {
        private readonly ISearchService _searchService;
        private readonly ILogger<ConditionalResourceHandler<TRequest, TResponse>> _logger;

        protected ConditionalResourceHandler(
             ISearchService searchService,
             IFhirDataStore fhirDataStore,
             Lazy<IConformanceProvider> conformanceProvider,
             IResourceWrapperFactory resourceWrapperFactory,
             ResourceIdProvider resourceIdProvider,
             IAuthorizationService<DataActions> authorizationService,
             ILogger<ConditionalResourceHandler<TRequest, TResponse>> logger)
            : base(fhirDataStore, conformanceProvider, resourceWrapperFactory, resourceIdProvider, authorizationService)
        {
            EnsureArg.IsNotNull(searchService, nameof(searchService));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _searchService = searchService;
            _logger = logger;
        }

        public async Task<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            // Get the required permissions for this specific conditional operation
            var requiredPermissions = GetRequiredPermissions(request);
            var granted = await AuthorizationService.CheckAccess(requiredPermissions.legacyPermissions | requiredPermissions.granularPermissions, cancellationToken);

            // Check if user has the required permissions:
            // 1. Legacy: Read + Write
            // 2. Granular: Search + Create/Update/Delete (specific combinations)
            bool hasLegacyPermissions = (granted & requiredPermissions.legacyPermissions) == requiredPermissions.legacyPermissions;
            bool hasGranularPermissions = (granted & requiredPermissions.granularPermissions) == requiredPermissions.granularPermissions;

            if (!hasLegacyPermissions && !hasGranularPermissions)
            {
                throw new UnauthorizedFhirActionException();
            }

            var results = await _searchService.ConditionalSearchAsync(
                request.ResourceType,
                request.ConditionalParameters,
                cancellationToken,
                logger: _logger);

            var matches = results.Results.Where(result => result.SearchEntryMode == ValueSets.SearchEntryMode.Match).ToList();
            int matchCount = matches.Count;
            if (matchCount == 0)
            {
                _logger.LogInformation("Conditional handler: Not Match. ResourceType={ResourceType}", request.ResourceType);
                return await HandleNoMatch(request, cancellationToken);
            }
            else if (matchCount == 1)
            {
                _logger.LogInformation("Conditional handler: One Match Found. ResourceType={ResourceType}", request.ResourceType);
                return await HandleSingleMatch(request, matches.First(), cancellationToken);
            }
            else
            {
                // Multiple matches: The server returns a 412 Precondition Failed error indicating the client's criteria were not selective enough
                _logger.LogInformation("PreconditionFailed: Conditional handler: Multiple Matches Found. ResourceType={ResourceType}, NumberOfMatches={NumberOfMatches}", request.ResourceType, matchCount);
                throw new PreconditionFailedException(string.Format(CultureInfo.InvariantCulture, Core.Resources.ConditionalOperationNotSelectiveEnough, request.ResourceType));
            }
        }

        /// <summary>
        /// Gets the required permissions for the specific conditional operation.
        /// Returns both legacy permissions (Read + Write) and granular permissions (Search + specific action).
        /// </summary>
        protected virtual (DataActions legacyPermissions, DataActions granularPermissions) GetRequiredPermissions(TRequest request)
        {
            // Default: Legacy Read+Write, Granular Search+Create/Update
            // Concrete implementations should override this to specify the exact granular permissions
            return (DataActions.Read | DataActions.Write, DataActions.Search | DataActions.Create | DataActions.Update);
        }

        public abstract Task<TResponse> HandleSingleMatch(TRequest request, SearchResultEntry match, CancellationToken cancellationToken);

        public abstract Task<TResponse> HandleNoMatch(TRequest request, CancellationToken cancellationToken);
    }
}
