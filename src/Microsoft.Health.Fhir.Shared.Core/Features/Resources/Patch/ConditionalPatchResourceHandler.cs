﻿// -------------------------------------------------------------------------------------------------
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
        private readonly ILogger<ConditionalPatchResourceHandler> _logger;

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
            EnsureArg.IsNotNull(logger, nameof(logger));

            _mediator = mediator;
            _logger = logger;
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
                _logger.LogInformation("PreconditionFailed: ResourceVersionConflict");
                throw new PreconditionFailedException(string.Format(Core.Resources.ResourceVersionConflict, request.WeakETag.VersionId));
            }

            var patchedResource = request.Payload.Patch(match.Resource);
            return await _mediator.Send<UpsertResourceResponse>(new UpsertResourceRequest(patchedResource, bundleResourceContext: null), cancellationToken);
        }

        /// <summary>
        /// Conditional patch requires search permissions (to find existing resources) and update permissions (for modifications).
        /// Legacy: Read + Write, SMART v2: Search + Update
        /// </summary>
        protected override (DataActions legacyPermissions, DataActions granularPermissions) GetRequiredPermissions(ConditionalPatchResourceRequest request)
        {
            return (DataActions.Read | DataActions.Write, DataActions.Search | DataActions.Update);
        }
    }
}
