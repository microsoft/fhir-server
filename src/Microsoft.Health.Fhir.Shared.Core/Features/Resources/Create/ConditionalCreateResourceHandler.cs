// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
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

namespace Microsoft.Health.Fhir.Core.Features.Resources.Create
{
    public class ConditionalCreateResourceHandler : BaseResourceHandler, IRequestHandler<ConditionalCreateResourceRequest, UpsertResourceResponse>
    {
        private readonly ISearchService _searchService;
        private readonly IMediator _mediator;

        public ConditionalCreateResourceHandler(
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

        public async Task<UpsertResourceResponse> Handle(ConditionalCreateResourceRequest message, CancellationToken cancellationToken = default)
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
                // No matches: The server creates the resource
                // TODO: There is a potential contention issue here in that this could create another new resource with a different id
                return await _mediator.Send<UpsertResourceResponse>(new CreateResourceRequest(message.Resource), cancellationToken);
            }
            else if (count == 1)
            {
                return null;
            }
            else
            {
                // Multiple matches: The server returns a 412 Precondition Failed error indicating the client's criteria were not selective enough
                throw new PreconditionFailedException(Core.Resources.ConditionalOperationNotSelectiveEnough);
            }
        }
    }
}
