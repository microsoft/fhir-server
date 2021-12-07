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
using Microsoft.Health.Fhir.Core.Messages;
using Microsoft.Health.Fhir.Core.Messages.Patch;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;

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
            IAuthorizationService<DataActions> authorizationService,
            IModelInfoProvider modelInfoProvider)
            : base(searchService, fhirDataStore, conformanceProvider, resourceWrapperFactory, resourceIdProvider, authorizationService)
        {
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            dynamic service;
            if (typeof(TData) == typeof(JsonPatchDocument))
                service = new JsonPatchService(modelInfoProvider);
            else if (typeof(TData) == typeof(Parameters))
                service = new FhirParameterPatchService(modelInfoProvider);
            else
                throw new ArgumentException($"Type {typeof(TData)} was not expected for this templated class");

            _patchService = service as AbstractPatchService<TData>;
            _mediator = mediator;
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
