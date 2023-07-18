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
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Delete;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Delete
{
    public class ConditionalDeleteResourceHandler : BaseResourceHandler, IRequestHandler<ConditionalDeleteResourceRequest, DeleteResourceResponse>
    {
        private readonly ISearchService _searchService;
        private readonly IDeletionService _deleter;
        private readonly FhirRequestContextAccessor _fhirContext;

        public ConditionalDeleteResourceHandler(
            IFhirDataStore fhirDataStore,
            Lazy<IConformanceProvider> conformanceProvider,
            IResourceWrapperFactory resourceWrapperFactory,
            ISearchService searchService,
            IMediator mediator,
            ResourceIdProvider resourceIdProvider,
            IAuthorizationService<DataActions> authorizationService,
            IDeletionService deleter,
            FhirRequestContextAccessor fhirContext)
            : base(fhirDataStore, conformanceProvider, resourceWrapperFactory, resourceIdProvider, authorizationService)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(searchService, nameof(searchService));
            EnsureArg.IsNotNull(deleter, nameof(deleter));

            _searchService = searchService;
            _deleter = deleter;
            _fhirContext = fhirContext;
        }

        public async Task<DeleteResourceResponse> Handle(ConditionalDeleteResourceRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            DataActions dataActions = (request.DeleteOperation == DeleteOperation.SoftDelete ? DataActions.Delete : DataActions.HardDelete | DataActions.Delete) | DataActions.Read;

            if (await AuthorizationService.CheckAccess(dataActions, cancellationToken) != dataActions)
            {
                throw new UnauthorizedFhirActionException();
            }

            try
            {
                if (request.MaxDeleteCount > 1)
                {
                    return await DeleteMultiple(request, cancellationToken);
                }

                return await DeleteSingle(request, cancellationToken);
            }
            catch (IncompleteOperationException<IReadOnlySet<string>> exception)
            {
                _fhirContext.RequestContext.ResponseHeaders[KnownHeaders.ItemsDeleted] = exception.PartialResults.Count.ToString();
                throw;
            }
        }

        private async Task<DeleteResourceResponse> DeleteSingle(ConditionalDeleteResourceRequest request, CancellationToken cancellationToken)
        {
            var matchedResults = await _searchService.ConditionalSearchAsync(request.ResourceType, request.ConditionalParameters, cancellationToken);

            int count = matchedResults.Results.Count;
            if (count == 0)
            {
                return new DeleteResourceResponse(0);
            }
            else if (count == 1)
            {
                var result = await _deleter.DeleteAsync(new DeleteResourceRequest(request.ResourceType, matchedResults.Results.First().Resource.ResourceId, request.DeleteOperation), cancellationToken);

                if (string.IsNullOrWhiteSpace(result.VersionId))
                {
                    return new DeleteResourceResponse(result);
                }

                return new DeleteResourceResponse(result, weakETag: WeakETag.FromVersionId(result.VersionId));
            }
            else
            {
                // Multiple matches: The server returns a 412 Precondition Failed error indicating the client's criteria were not selective enough
                throw new PreconditionFailedException(string.Format(CultureInfo.InvariantCulture, Core.Resources.ConditionalOperationNotSelectiveEnough, request.ResourceType));
            }
        }

        private async Task<DeleteResourceResponse> DeleteMultiple(ConditionalDeleteResourceRequest request, CancellationToken cancellationToken)
        {
            IReadOnlySet<string> itemsDeleted = await _deleter.DeleteMultipleAsync(request, cancellationToken);
            return new DeleteResourceResponse(itemsDeleted.Count);
        }
    }
}
