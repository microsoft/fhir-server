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
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Versions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Bundle;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Messages.Get;
using Microsoft.Health.Fhir.Core.Messages.Patch;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class FhirMediatorExtensions
    {
        public static async Task<RawResourceElement> CreateResourceAsync(this IMediator mediator, ResourceElement resource, CancellationToken cancellationToken = default)
        {
            return await CreateResourceAsync(mediator, new CreateResourceRequest(resource), cancellationToken);
        }

        public static async Task<RawResourceElement> CreateResourceAsync(this IMediator mediator, CreateResourceRequest createResourceRequest, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(createResourceRequest, nameof(createResourceRequest));

            UpsertResourceResponse result = await mediator.Send<UpsertResourceResponse>(createResourceRequest, cancellationToken);

            return result.Outcome.RawResourceElement;
        }

        public static async Task<SaveOutcome> UpsertResourceAsync(this IMediator mediator, ResourceElement resource, WeakETag weakETag = null, CancellationToken cancellationToken = default)
        {
            return await UpsertResourceAsync(mediator, new UpsertResourceRequest(resource, weakETag: weakETag), cancellationToken);
        }

        public static async Task<SaveOutcome> UpsertResourceAsync(this IMediator mediator, UpsertResourceRequest upsertResourceRequest, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(upsertResourceRequest, nameof(upsertResourceRequest));

            UpsertResourceResponse result = await mediator.Send<UpsertResourceResponse>(upsertResourceRequest, cancellationToken);

            return result.Outcome;
        }

        public static async Task<RawResourceElement> GetResourceAsync(this IMediator mediator, ResourceKey key, CancellationToken cancellationToken = default)
        {
            return await GetResourceAsync(mediator, new GetResourceRequest(key), cancellationToken);
        }

        public static async Task<RawResourceElement> GetResourceAsync(this IMediator mediator, GetResourceRequest getResourceRequest, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(getResourceRequest, nameof(getResourceRequest));

            GetResourceResponse result = await mediator.Send(getResourceRequest, cancellationToken);

            return result.Resource;
        }

        public static async Task<DeleteResourceResponse> DeleteResourceAsync(this IMediator mediator, ResourceKey key, DeleteOperation deleteOperation, CancellationToken cancellationToken = default)
        {
            return await DeleteResourceAsync(mediator, new DeleteResourceRequest(key, deleteOperation), cancellationToken);
        }

        public static async Task<DeleteResourceResponse> DeleteResourceAsync(this IMediator mediator, DeleteResourceRequest deleteResourceRequest, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(deleteResourceRequest, nameof(deleteResourceRequest));

            var result = await mediator.Send(deleteResourceRequest, cancellationToken);

            return result;
        }

        public static async Task<UpsertResourceResponse> PatchResourceAsync(this IMediator mediator, PatchResourceRequest patchResourceRequest, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(patchResourceRequest, nameof(patchResourceRequest));

            UpsertResourceResponse result = await mediator.Send(patchResourceRequest, cancellationToken);

            return result;
        }

        public static async Task<UpsertResourceResponse> ConditionalPatchResourceAsync(this IMediator mediator, ConditionalPatchResourceRequest conditionalPatchResourceRequest, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(conditionalPatchResourceRequest, nameof(conditionalPatchResourceRequest));

            UpsertResourceResponse result = await mediator.Send(conditionalPatchResourceRequest, cancellationToken);

            return result;
        }

        public static async Task<ResourceElement> SearchResourceAsync(this IMediator mediator, string type, IReadOnlyList<Tuple<string, string>> queries, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            var result = await mediator.Send(new SearchResourceRequest(type, queries), cancellationToken);

            return result.Bundle;
        }

        public static async Task<ResourceElement> SearchResourceHistoryAsync(this IMediator mediator, PartialDateTime since = null, PartialDateTime before = null, PartialDateTime at = null, int? count = null, string continuationToken = null, string sort = null, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            var result = await mediator.Send(new SearchResourceHistoryRequest(since, before, at, count, continuationToken, sort), cancellationToken);

            return result.Bundle;
        }

        public static async Task<ResourceElement> SearchResourceHistoryAsync(this IMediator mediator, string resourceType, PartialDateTime since = null, PartialDateTime before = null, PartialDateTime at = null, int? count = null, string continuationToken = null, string sort = null, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            var result = await mediator.Send(new SearchResourceHistoryRequest(resourceType, since, before, at, count, continuationToken, sort), cancellationToken);

            return result.Bundle;
        }

        public static async Task<ResourceElement> SearchResourceHistoryAsync(this IMediator mediator, string resourceType, string resourceId, PartialDateTime since = null, PartialDateTime before = null, PartialDateTime at = null, int? count = null, string continuationToken = null, string sort = null, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            var result = await mediator.Send(new SearchResourceHistoryRequest(resourceType, resourceId, since, before, at, count, continuationToken, sort), cancellationToken);

            return result.Bundle;
        }

        public static async Task<ResourceElement> SearchResourceCompartmentAsync(this IMediator mediator, string compartmentType, string compartmentId, string resourceType, IReadOnlyList<Tuple<string, string>> queries, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            var result = await mediator.Send(new SearchCompartmentRequest(compartmentType, compartmentId, resourceType, queries), cancellationToken);

            return result.Bundle;
        }

        public static async Task<ResourceElement> GetCapabilitiesAsync(this IMediator mediator, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            var response = await mediator.Send(new GetCapabilitiesRequest(), cancellationToken);
            return response.CapabilityStatement;
        }

        public static async Task<SmartConfigurationResult> GetSmartConfigurationAsync(this IMediator mediator, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            var response = await mediator.Send(new GetSmartConfigurationRequest(), cancellationToken);

            return new SmartConfigurationResult(response.AuthorizationEndpoint, response.TokenEndpoint, response.Capabilities);
        }

        public static async Task<VersionsResult> GetOperationVersionsAsync(this IMediator mediator, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            var response = await mediator.Send(new GetOperationVersionsRequest(), cancellationToken);

            return new VersionsResult(response.SupportedVersions, response.DefaultVersion);
        }

        public static async Task<ResourceElement> PostBundle(this IMediator mediator, ResourceElement bundle, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(bundle, nameof(bundle));

            BundleResponse result = await mediator.Send<BundleResponse>(new BundleRequest(bundle), cancellationToken);

            return result.Bundle;
        }
    }
}
