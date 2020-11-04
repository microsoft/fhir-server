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
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Bundle;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Messages.Get;
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

        public static async Task<DeleteResourceResponse> DeleteResourceAsync(this IMediator mediator, ResourceKey key, bool hardDelete, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(key, nameof(key));

            var result = await mediator.Send(new DeleteResourceRequest(key, hardDelete), cancellationToken);

            return result;
        }

        public static async Task<ResourceElement> SearchResourceAsync(this IMediator mediator, string type, IReadOnlyList<Tuple<string, string>> queries, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            var result = await mediator.Send(new SearchResourceRequest(type, queries), cancellationToken);

            return result.Bundle;
        }

        public static async Task<ResourceElement> SearchResourceHistoryAsync(this IMediator mediator, PartialDateTime since = null, PartialDateTime before = null, PartialDateTime at = null, int? count = null, string continuationToken = null, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            var result = await mediator.Send(new SearchResourceHistoryRequest(since, before, at, count, continuationToken), cancellationToken);

            return result.Bundle;
        }

        public static async Task<ResourceElement> SearchResourceHistoryAsync(this IMediator mediator, string resourceType, PartialDateTime since = null, PartialDateTime before = null, PartialDateTime at = null, int? count = null, string continuationToken = null, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            var result = await mediator.Send(new SearchResourceHistoryRequest(resourceType, since, before, at, count, continuationToken), cancellationToken);

            return result.Bundle;
        }

        public static async Task<ResourceElement> SearchResourceHistoryAsync(this IMediator mediator, string resourceType, string resourceId, PartialDateTime since = null, PartialDateTime before = null, PartialDateTime at = null, int? count = null, string continuationToken = null, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            var result = await mediator.Send(new SearchResourceHistoryRequest(resourceType, resourceId, since, before, at, count, continuationToken), cancellationToken);

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

        public static async Task<ResourceElement> PostBundle(this IMediator mediator, ResourceElement bundle, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(bundle, nameof(bundle));

            BundleResponse result = await mediator.Send<BundleResponse>(new BundleRequest(bundle), cancellationToken);

            return result.Bundle;
        }
    }
}
