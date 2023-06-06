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
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Delete;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Delete
{
    public class ConditionalDeleteResourceHandler : BaseResourceHandler, IRequestHandler<ConditionalDeleteResourceRequest, DeleteResourceResponse>
    {
        private readonly ISearchService _searchService;
        private readonly IMediator _mediator;

        public ConditionalDeleteResourceHandler(
            IFhirDataStore fhirDataStore,
            Lazy<IConformanceProvider> conformanceProvider,
            IResourceWrapperFactory resourceWrapperFactory,
            ISearchService searchService,
            IMediator mediator,
            ResourceIdProvider resourceIdProvider,
            IAuthorizationService<DataActions> authorizationService)
            : base(fhirDataStore, conformanceProvider, resourceWrapperFactory, resourceIdProvider, authorizationService)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(searchService, nameof(searchService));

            _searchService = searchService;
            _mediator = mediator;
        }

        public async Task<DeleteResourceResponse> Handle(ConditionalDeleteResourceRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            DataActions dataActions = (request.DeleteOperation == DeleteOperation.SoftDelete ? DataActions.Delete : DataActions.HardDelete | DataActions.Delete) | DataActions.Read;

            if (await AuthorizationService.CheckAccess(dataActions, cancellationToken) != dataActions)
            {
                throw new UnauthorizedFhirActionException();
            }

            if (request.MaxDeleteCount > 1)
            {
                return await DeleteMultiple(request, cancellationToken);
            }

            return await DeleteSingle(request, cancellationToken);
        }

        private async Task<DeleteResourceResponse> DeleteSingle(ConditionalDeleteResourceRequest request, CancellationToken cancellationToken)
        {
            IReadOnlyCollection<SearchResultEntry> matchedResults = await _searchService.ConditionalSearchAsync(request.ResourceType, request.ConditionalParameters, cancellationToken);

            int count = matchedResults.Count;
            if (count == 0)
            {
                return new DeleteResourceResponse(0);
            }
            else if (count == 1)
            {
                return await _mediator.Send(new DeleteResourceRequest(request.ResourceType, matchedResults.First().Resource.ResourceId, request.DeleteOperation, bundleOperationId: request.BundleOperationId), cancellationToken);
            }
            else
            {
                // Multiple matches: The server returns a 412 Precondition Failed error indicating the client's criteria were not selective enough
                throw new PreconditionFailedException(string.Format(CultureInfo.InvariantCulture, Core.Resources.ConditionalOperationNotSelectiveEnough, request.ResourceType));
            }
        }

        private async Task<DeleteResourceResponse> DeleteMultiple(ConditionalDeleteResourceRequest request, CancellationToken cancellationToken)
        {
            (IReadOnlyCollection<SearchResultEntry> matchedResults, string ct) = await _searchService.ConditionalSearchAsync(request.ResourceType, request.ConditionalParameters, request.MaxDeleteCount, cancellationToken);

            int itemsDeleted = 0;

            // Delete the matched results...
            while (matchedResults.Any() || !string.IsNullOrEmpty(ct))
            {
                foreach (SearchResultEntry item in matchedResults.Take(request.MaxDeleteCount - itemsDeleted))
                {
                    DeleteResourceResponse result = await _mediator.Send(new DeleteResourceRequest(request.ResourceType, item.Resource.ResourceId, request.DeleteOperation, request.BundleOperationId), cancellationToken);
                    itemsDeleted += result.ResourcesDeleted;
                }

                if (!string.IsNullOrEmpty(ct) && request.MaxDeleteCount - itemsDeleted > 0)
                {
                    (matchedResults, ct) = await _searchService.ConditionalSearchAsync(
                        request.ResourceType,
                        request.ConditionalParameters,
                        request.MaxDeleteCount - itemsDeleted,
                        cancellationToken,
                        ct);
                }
                else
                {
                    break;
                }
            }

            return new DeleteResourceResponse(itemsDeleted);
        }
    }
}
