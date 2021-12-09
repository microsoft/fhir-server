// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Patch;
using Microsoft.Health.Fhir.Core.Messages.Upsert;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Patch
{
    public sealed class ConditionalPatchResourceHandler<TData> : ConditionalResourceHandler<ConditionalPatchResourceRequest<TData>, UpsertResourceResponse>
    {
        private readonly AbstractPatchService<TData> _patchService;
        private readonly IMediator _mediator;

        public ConditionalPatchResourceHandler(
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

            if (typeof(TData) == typeof(JsonPatchDocument))
            {
                _patchService = new JsonPatchService() as AbstractPatchService<TData>;
                return;
            }
            else if (typeof(TData) == typeof(Parameters))
            {
                _patchService = new FhirParameterPatchService() as AbstractPatchService<TData>;
                return;
            }

            throw new ArgumentException($"Type {typeof(TData)} was not expected for this templated class");
        }

        public override Task<UpsertResourceResponse> HandleNoMatch(ConditionalPatchResourceRequest<TData> request, CancellationToken cancellationToken)
        {
            throw new ResourceNotFoundException("Resource not found");
        }

        public override async Task<UpsertResourceResponse> HandleSingleMatch(ConditionalPatchResourceRequest<TData> request, SearchResultEntry match, CancellationToken cancellationToken)
        {
            TData resource = request.PatchDocument;
            var patchedResource = _patchService.Patch(match.Resource, resource, request.WeakETag);
            return await _mediator.Send<UpsertResourceResponse>(new UpsertResourceRequest(patchedResource), cancellationToken);
        }
    }
}
