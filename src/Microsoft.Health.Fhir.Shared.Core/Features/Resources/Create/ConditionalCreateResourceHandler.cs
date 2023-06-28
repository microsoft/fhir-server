// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security;

using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Upsert;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Create
{
    public sealed class ConditionalCreateResourceHandler : ConditionalResourceHandler<ConditionalCreateResourceRequest, UpsertResourceResponse>
    {
        private readonly IMediator _mediator;

        public ConditionalCreateResourceHandler(
            IFhirDataStore fhirDataStore,
            Lazy<IConformanceProvider> conformanceProvider,
            IResourceWrapperFactory resourceWrapperFactory,
            ISearchService searchService,
            IMediator mediator,
            ResourceIdProvider resourceIdProvider,
            IAuthorizationService<DataActions> authorizationService)
            : base(searchService, fhirDataStore, conformanceProvider, resourceWrapperFactory, resourceIdProvider, authorizationService)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            _mediator = mediator;
        }

        public override async Task<UpsertResourceResponse> HandleNoMatch(ConditionalCreateResourceRequest request, CancellationToken cancellationToken)
        {
            return await _mediator.Send<UpsertResourceResponse>(new CreateResourceRequest(request.Resource, request.BundleResourceContext), cancellationToken);
        }

        public override Task<UpsertResourceResponse> HandleSingleMatch(ConditionalCreateResourceRequest request, SearchResultEntry match, CancellationToken cancellationToken)
        {
            return Task.FromResult<UpsertResourceResponse>(null);
        }
    }
}
