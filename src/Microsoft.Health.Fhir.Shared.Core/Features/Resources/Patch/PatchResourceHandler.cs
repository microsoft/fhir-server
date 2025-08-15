﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Patch;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Patch
{
    public class PatchResourceHandler : BaseResourceHandler, IRequestHandler<PatchResourceRequest, UpsertResourceResponse>
    {
        private readonly IMediator _mediator;
        private readonly ILogger<PatchResourceHandler> _logger;

        public PatchResourceHandler(
            IMediator mediator,
            IFhirDataStore fhirDataStore,
            Lazy<IConformanceProvider> conformanceProvider,
            IResourceWrapperFactory resourceWrapperFactory,
            ResourceIdProvider resourceIdProvider,
            IAuthorizationService<DataActions> authorizationService,
            ILogger<PatchResourceHandler> logger)
            : base(fhirDataStore, conformanceProvider, resourceWrapperFactory, resourceIdProvider, authorizationService)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _mediator = mediator;
            _logger = logger;
        }

        public async Task<UpsertResourceResponse> Handle(PatchResourceRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            // Check for granular permissions (Update) or legacy permissions (Read + Write)
            var granted = await AuthorizationService.CheckAccess(DataActions.Update | DataActions.Read | DataActions.Write, cancellationToken);
            if ((granted & DataActions.Update) != DataActions.Update &&
                (granted & (DataActions.Read | DataActions.Write)) != (DataActions.Read | DataActions.Write))
            {
                throw new UnauthorizedFhirActionException();
            }

            ResourceKey key = request.ResourceKey;

            if (!string.IsNullOrEmpty(key.VersionId))
            {
                throw new MethodNotAllowedException(Core.Resources.DeleteVersionNotAllowed);
            }

            ResourceWrapper currentDoc = await FhirDataStore.GetAsync(key, cancellationToken);
            if (currentDoc == null)
            {
                throw new ResourceNotFoundException(string.Format(Core.Resources.ResourceNotFoundById, key.ResourceType, key.Id));
            }

            if (currentDoc.IsHistory)
            {
                throw new MethodNotAllowedException(Core.Resources.PatchVersionNotAllowed);
            }

            if (request.WeakETag != null && request.WeakETag.VersionId != currentDoc.Version)
            {
                _logger.LogInformation("PreconditionFailed: ResourceVersionConflict");
                throw new PreconditionFailedException(string.Format(Core.Resources.ResourceVersionConflict, request.WeakETag.VersionId));
            }

            ResourceElement patchedResource = request.Payload.Patch(currentDoc);
            return await _mediator.Send<UpsertResourceResponse>(new UpsertResourceRequest(patchedResource, request.BundleResourceContext, request.WeakETag), cancellationToken);
        }
    }
}
