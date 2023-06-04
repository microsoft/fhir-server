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
using Microsoft.Health.Fhir.Core.Features.Resources.Patch;
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
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(resource, nameof(resource));

            UpsertResourceResponse result = await mediator.Send<UpsertResourceResponse>(new CreateResourceRequest(resource), cancellationToken);

            return result.Outcome.RawResourceElement;
        }

        public static async Task<SaveOutcome> UpsertResourceAsync(this IMediator mediator, ResourceElement resource, WeakETag weakETag = null, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(resource, nameof(resource));

            UpsertResourceResponse result = await mediator.Send<UpsertResourceResponse>(new UpsertResourceRequest(resource, weakETag), cancellationToken);

            return result.Outcome;
        }

        public static async Task<RawResourceElement> GetResourceAsync(this IMediator mediator, ResourceKey key, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(key, nameof(key));

            GetResourceResponse result = await mediator.Send(new GetResourceRequest(key), cancellationToken);

            return result.Resource;
        }

        public static async Task<DeleteResourceResponse> DeleteResourceAsync(this IMediator mediator, ResourceKey key, DeleteOperation deleteOperation, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(key, nameof(key));

            var result = await mediator.Send(new DeleteResourceRequest(key, deleteOperation), cancellationToken);

            return result;
        }

        public static async Task<UpsertResourceResponse> PatchResourceAsync(this IMediator mediator, ResourceKey key, PatchPayload payload, WeakETag weakETag = null, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(key, nameof(key));
            EnsureArg.IsNotNull(payload, nameof(payload));

            UpsertResourceResponse result = await mediator.Send(new PatchResourceRequest(key, payload, weakETag), cancellationToken);

            return result;
        }

        public static async Task<UpsertResourceResponse> ConditionalPatchResourceAsync(this IMediator mediator, string typeParameter, PatchPayload payload, IReadOnlyList<Tuple<string, string>> conditionalParameters, WeakETag weakETag = null, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(typeParameter, nameof(typeParameter));
            EnsureArg.IsNotNull(payload, nameof(payload));
            EnsureArg.IsNotNull(conditionalParameters, nameof(conditionalParameters));

            UpsertResourceResponse result = await mediator.Send(new ConditionalPatchResourceRequest(typeParameter, payload, conditionalParameters, weakETag), cancellationToken);

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
