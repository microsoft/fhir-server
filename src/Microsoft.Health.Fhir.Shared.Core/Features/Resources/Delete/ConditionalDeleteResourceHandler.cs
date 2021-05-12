// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Configs;
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
        private readonly int _conditionalDeleteMaxItems;

        public ConditionalDeleteResourceHandler(
            IFhirDataStore fhirDataStore,
            Lazy<IConformanceProvider> conformanceProvider,
            IResourceWrapperFactory resourceWrapperFactory,
            ISearchService searchService,
            IMediator mediator,
            ResourceIdProvider resourceIdProvider,
            IAuthorizationService<DataActions> authorizationService,
            IOptions<CoreFeatureConfiguration> featureConfiguration)
            : base(fhirDataStore, conformanceProvider, resourceWrapperFactory, resourceIdProvider, authorizationService)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(searchService, nameof(searchService));
            EnsureArg.IsNotNull(featureConfiguration?.Value, nameof(featureConfiguration));

            _searchService = searchService;
            _mediator = mediator;
            _conditionalDeleteMaxItems = featureConfiguration.Value.ConditionalDeleteMaxItems;
        }

        public async Task<DeleteResourceResponse> Handle(ConditionalDeleteResourceRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            DataActions dataActions = (request.HardDelete ? DataActions.Delete | DataActions.HardDelete : DataActions.Delete) | DataActions.Read;

            if (await AuthorizationService.CheckAccess(dataActions, cancellationToken) != dataActions)
            {
                throw new UnauthorizedFhirActionException();
            }

            if (request.DeleteMultiple)
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
                return null;
            }
            else if (count == 1)
            {
                return await _mediator.Send(new DeleteResourceRequest(request.ResourceType, matchedResults.First().Resource.ResourceId, request.HardDelete), cancellationToken);
            }
            else
            {
                // Multiple matches: The server returns a 412 Precondition Failed error indicating the client's criteria were not selective enough
                throw new PreconditionFailedException(Core.Resources.ConditionalOperationNotSelectiveEnough);
            }
        }

        private async Task<DeleteResourceResponse> DeleteMultiple(ConditionalDeleteResourceRequest request, CancellationToken cancellationToken)
        {
            (IReadOnlyCollection<SearchResultEntry> matchedResults, string ct) = await _searchService.ConditionalSearchAsync(request.ResourceType, request.ConditionalParameters, _conditionalDeleteMaxItems, cancellationToken);

            int itemsDeleted = 0;

            // Delete the matched results...
            while (matchedResults.Any() || !string.IsNullOrEmpty(ct))
            {
                foreach (IEnumerable<SearchResultEntry> batch in matchedResults.Take(_conditionalDeleteMaxItems - itemsDeleted).TakeBatch(10))
                {
                    DeleteResourceResponse[] results = await Task.WhenAll(batch.Select(result => _mediator.Send(new DeleteResourceRequest(request.ResourceType, result.Resource.ResourceId, request.HardDelete), cancellationToken)));
                    itemsDeleted += results.Sum(x => x.ResourcesDeleted);
                }

                if (!string.IsNullOrEmpty(ct) && _conditionalDeleteMaxItems - itemsDeleted > 0)
                {
                    (matchedResults, ct) = await _searchService.ConditionalSearchAsync(
                        request.ResourceType,
                        request.ConditionalParameters,
                        _conditionalDeleteMaxItems - itemsDeleted,
                        cancellationToken,
                        ct);
                }
                else
                {
                    matchedResults = Array.Empty<SearchResultEntry>();
                }
            }

            return new DeleteResourceResponse(itemsDeleted);
        }
    }
}
