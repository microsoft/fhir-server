// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Build.Framework;
using Microsoft.Extensions.Logging;
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
    public class ConditionalPatchResourceHandler : ConditionalResourceHandler<ConditionalPatchResourceRequest, UpsertResourceResponse>
    {
        private readonly IMediator _mediator;

        public ConditionalPatchResourceHandler(
            IFhirDataStore fhirDataStore,
            Lazy<IConformanceProvider> conformanceProvider,
            IResourceWrapperFactory resourceWrapperFactory,
            ISearchService searchService,
            IMediator mediator,
            ResourceIdProvider resourceIdProvider,
            IAuthorizationService<DataActions> authorizationService,
            ILogger<ConditionalPatchResourceHandler> logger)
            : base(searchService, fhirDataStore, conformanceProvider, resourceWrapperFactory, resourceIdProvider, authorizationService, logger)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            _mediator = mediator;
        }

        public override Task<UpsertResourceResponse> HandleNoMatch(ConditionalPatchResourceRequest request, CancellationToken cancellationToken)
        {
            throw new ResourceNotFoundException("Resource not found");
        }

        public override async Task<UpsertResourceResponse> HandleSingleMatch(ConditionalPatchResourceRequest request, SearchResultEntry match, CancellationToken cancellationToken)
        {
            if (match.Resource.IsHistory)
            {
                throw new MethodNotAllowedException(Core.Resources.PatchVersionNotAllowed);
            }

            if (request.WeakETag != null && request.WeakETag.VersionId != match.Resource.Version)
            {
                throw new PreconditionFailedException(string.Format(Core.Resources.ResourceVersionConflict, request.WeakETag.VersionId));
            }

            var patchedResource = request.Payload.Patch(match.Resource);
            return await _mediator.Send<UpsertResourceResponse>(new UpsertResourceRequest(patchedResource, bundleResourceContext: null), cancellationToken);
        }
    }
}
