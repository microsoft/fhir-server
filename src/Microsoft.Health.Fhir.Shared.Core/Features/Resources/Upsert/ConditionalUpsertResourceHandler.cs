// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using Medino;
using Microsoft.Build.Framework;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core.Features.Security.Authorization;
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
    public sealed class ConditionalUpsertResourceHandler : ConditionalResourceHandler<ConditionalUpsertResourceRequest, UpsertResourceResponse>
    {
        private readonly IMediator _mediator;
        private readonly ILogger<ConditionalUpsertResourceHandler> _logger;

        public ConditionalUpsertResourceHandler(
            IFhirDataStore fhirDataStore,
            Lazy<IConformanceProvider> conformanceProvider,
            IResourceWrapperFactory resourceWrapperFactory,
            ISearchService searchService,
            IMediator mediator,
            ResourceIdProvider resourceIdProvider,
            IAuthorizationService<DataActions> authorizationService,
            ILogger<ConditionalUpsertResourceHandler> logger)
            : base(searchService, fhirDataStore, conformanceProvider, resourceWrapperFactory, resourceIdProvider, authorizationService, logger)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _mediator = mediator;
            _logger = logger;
        }

        public override async Task<UpsertResourceResponse> HandleNoMatch(ConditionalUpsertResourceRequest request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(request.Resource.Id))
            {
                // No matches, no id provided: The server creates the resource.
                // A brand new resource cannot satisfy any client version, and silently ignoring the header would
                // turn an explicitly guarded request into an unguarded create. Reject before creating anything.
                if (request.WeakETag != null)
                {
                    _logger.LogInformation("PreconditionFailed: ConditionalUpdateNoMatchWithIfMatch");
                    throw new PreconditionFailedException(string.Format(
                        CultureInfo.InvariantCulture,
                        Core.Resources.ConditionalUpdateNoMatchWithIfMatch,
                        request.ResourceType,
                        request.WeakETag.VersionId));
                }

                // TODO: There is a potential contention issue here in that this could create another new resource with a different id
                return await _mediator.SendAsync<UpsertResourceResponse>(new CreateResourceRequest(request.Resource, request.BundleResourceContext), cancellationToken);
            }
            else
            {
                // No matches, id provided: The server treats the interaction as an Update as Create interaction (or rejects it, if it does not support Update as Create).
                // The row addressed by that id was never inspected by the conditional search, so it may exist and
                // may be newer than the client's version. Persistence is the only component that can compare the
                // two, so the client ETag must be forwarded rather than dropped.
                // TODO: There is a potential contention issue here that this could replace an existing resource
                return await _mediator.SendAsync<UpsertResourceResponse>(
                    new UpsertResourceRequest(
                        request.Resource,
                        request.BundleResourceContext,
                        weakETag: request.WeakETag),
                    cancellationToken);
            }
        }

        public override async Task<UpsertResourceResponse> HandleSingleMatch(ConditionalUpsertResourceRequest request, SearchResultEntry match, CancellationToken cancellationToken)
        {
            ResourceWrapper resourceWrapper = match.Resource;
            Resource resource = request.Resource.ToPoco();

            if (request.WeakETag != null && request.WeakETag.VersionId != resourceWrapper.Version)
            {
                throw new PreconditionFailedException(string.Format(Core.Resources.ResourceVersionConflict, request.WeakETag.VersionId));
            }

            // One Match, no resource id provided OR (resource id provided and it matches the found resource): The server performs the update against the matching resource
            if (!string.IsNullOrEmpty(resource.Id) && !string.Equals(resource.Id, resourceWrapper.ResourceId, StringComparison.Ordinal))
            {
                throw new BadRequestException(string.Format(Core.Resources.ConditionalUpdateMismatchedIds, resourceWrapper.ResourceId, resource.Id));
            }

            // The version the conditional search observed is forwarded as the internal ComparedVersion guard so
            // persistence can reject a write racing between the search and the update. If the data store
            // projection did not surface one, that guard cannot be built at all, and continuing would silently
            // downgrade a guarded write into an unguarded one. Fail closed, mirroring the conditional delete
            // Match-version handling, rather than dropping the CAS.
            if (string.IsNullOrEmpty(resourceWrapper.Version))
            {
                _logger.LogInformation("PreconditionFailed: ConditionalUpdateMatchVersionUnavailable");
                throw new PreconditionFailedException(string.Format(
                    CultureInfo.InvariantCulture,
                    Core.Resources.ConditionalUpdateMatchVersionUnavailable,
                    resourceWrapper.ResourceTypeName,
                    resourceWrapper.ResourceId));
            }

            resource.Id = resourceWrapper.ResourceId;
            return await _mediator.SendAsync<UpsertResourceResponse>(
                new UpsertResourceRequest(
                    resource.ToResourceElement(),
                    request.BundleResourceContext,
                    weakETag: request.WeakETag,
                    comparedVersion: resourceWrapper.Version),
                cancellationToken);
        }

        public override Task<bool> CheckAccess(CancellationToken cancellationToken)
        {
            return AuthorizationService.CheckConditionalUpdateAccess(cancellationToken);
        }
    }
}
