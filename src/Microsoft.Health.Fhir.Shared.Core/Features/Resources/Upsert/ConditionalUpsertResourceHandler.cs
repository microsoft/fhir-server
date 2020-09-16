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
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Upsert;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Upsert
{
    /// <summary>
    /// Handles Conditional Update logic as defined in the spec https://www.hl7.org/fhir/http.html#cond-update
    /// </summary>
    public class ConditionalUpsertResourceHandler
        : BaseResourceHandler, IRequestHandler<ConditionalUpsertResourceRequest, UpsertResourceResponse>
    {
        private readonly ISearchService _searchService;
        private readonly IMediator _mediator;

        public ConditionalUpsertResourceHandler(
            IFhirDataStore fhirDataStore,
            Lazy<IConformanceProvider> conformanceProvider,
            IResourceWrapperFactory resourceWrapperFactory,
            ISearchService searchService,
            IMediator mediator,
            ResourceIdProvider resourceIdProvider,
            IFhirAuthorizationService authorizationService)
            : base(fhirDataStore, conformanceProvider, resourceWrapperFactory, resourceIdProvider, authorizationService)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(searchService, nameof(searchService));

            _searchService = searchService;
            _mediator = mediator;
        }

        public async Task<UpsertResourceResponse> Handle(ConditionalUpsertResourceRequest message, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(message, nameof(message));

            if (await AuthorizationService.CheckAccess(DataActions.Read | DataActions.Write) != (DataActions.Read | DataActions.Write))
            {
                throw new UnauthorizedFhirActionException();
            }

            IReadOnlyCollection<SearchResultEntry> matchedResults = await _searchService.ConditionalSearchAsync(message.Resource.InstanceType, message.ConditionalParameters, cancellationToken);

            int count = matchedResults.Count;
            if (count == 0)
            {
                if (string.IsNullOrEmpty(message.Resource.Id))
                {
                    // No matches, no id provided: The server creates the resource
                    // TODO: There is a potential contention issue here in that this could create another new resource with a different id
                    return await _mediator.Send<UpsertResourceResponse>(new CreateResourceRequest(message.Resource), cancellationToken);
                }
                else
                {
                    // No matches, id provided: The server treats the interaction as an Update as Create interaction (or rejects it, if it does not support Update as Create)
                    // TODO: There is a potential contention issue here that this could replace an existing resource
                    return await _mediator.Send<UpsertResourceResponse>(new UpsertResourceRequest(message.Resource), cancellationToken);
                }
            }
            else if (count == 1)
            {
                ResourceWrapper resourceWrapper = matchedResults.First().Resource;
                Resource resource = message.Resource.ToPoco();
                var version = WeakETag.FromVersionId(resourceWrapper.Version);

                // One Match, no resource id provided OR (resource id provided and it matches the found resource): The server performs the update against the matching resource
                if (string.IsNullOrEmpty(resource.Id) || string.Equals(resource.Id, resourceWrapper.ResourceId, StringComparison.Ordinal))
                {
                    resource.Id = resourceWrapper.ResourceId;
                    return await _mediator.Send<UpsertResourceResponse>(new UpsertResourceRequest(resource.ToResourceElement(), version), cancellationToken);
                }
                else
                {
                    throw new BadRequestException(string.Format(Core.Resources.ConditionalUpdateMismatchedIds, resourceWrapper.ResourceId, resource.Id));
                }
            }
            else
            {
                // Multiple matches: The server returns a 412 Precondition Failed error indicating the client's criteria were not selective enough
                throw new PreconditionFailedException(Core.Resources.ConditionalOperationNotSelectiveEnough);
            }
        }
    }
}
