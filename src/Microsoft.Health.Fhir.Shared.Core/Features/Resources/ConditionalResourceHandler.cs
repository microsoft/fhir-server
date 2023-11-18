// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
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

        public async Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await AuthorizationService.CheckAccess(DataActions.Read | DataActions.Write, cancellationToken) != (DataActions.Read | DataActions.Write))
            {
                throw new UnauthorizedFhirActionException();
            }

            var matchedResults = await _searchService.ConditionalSearchAsync(request.ResourceType, request.ConditionalParameters, cancellationToken);

            int count = matchedResults.Results.Count;
            if (count == 0)
            {
                _logger.LogInformation("Conditional handler: Not Match. ResourceType={ResourceType}", request.ResourceType);
                return await HandleNoMatch(request,  cancellationToken);
            }
            else if (count == 1)
            {
                _logger.LogInformation("Conditional handler: One Match Found. ResourceType={ResourceType}", request.ResourceType);
                return await HandleSingleMatch(request, matchedResults.Results.First(), cancellationToken);
            }
            else
            {
                // Multiple matches: The server returns a 412 Precondition Failed error indicating the client's criteria were not selective enough
                _logger.LogInformation("PreconditionFailed - Conditional handler: Multiple Matches Found. ResourceType={ResourceType}, NumberOfMatches={NumberOfMatches}", request.ResourceType, count);
                throw new PreconditionFailedException(string.Format(CultureInfo.InvariantCulture, Core.Resources.ConditionalOperationNotSelectiveEnough, request.ResourceType));
            }
        }

        public abstract Task<TResponse> HandleSingleMatch(TRequest request, SearchResultEntry match, CancellationToken cancellationToken);

        public abstract Task<TResponse> HandleNoMatch(TRequest request, CancellationToken cancellationToken);
    }
}
