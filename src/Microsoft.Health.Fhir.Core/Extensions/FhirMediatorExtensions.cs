// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Messages.Export;
using Microsoft.Health.Fhir.Core.Messages.Get;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class FhirMediatorExtensions
    {
        public static async Task<Resource> CreateResourceAsync(this IMediator mediator, Resource resource, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(resource, nameof(resource));

            UpsertResourceResponse result = await mediator.Send<UpsertResourceResponse>(new CreateResourceRequest(resource), cancellationToken);

            return result.Outcome.Resource;
        }

        public static async Task<SaveOutcome> UpsertResourceAsync(this IMediator mediator, Resource resource, WeakETag weakETag = null, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(resource, nameof(resource));

            UpsertResourceResponse result = await mediator.Send<UpsertResourceResponse>(new UpsertResourceRequest(resource, weakETag), cancellationToken);

            return result.Outcome;
        }

        public static async Task<Resource> GetResourceAsync(this IMediator mediator, ResourceKey key, CancellationToken cancellationToken = default)
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

        public static async Task<Bundle> SearchResourceAsync(this IMediator mediator, string type, IReadOnlyList<Tuple<string, string>> queries, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            var result = await mediator.Send(new SearchResourceRequest(type, queries), cancellationToken);

            return result.Bundle;
        }

        public static async Task<Bundle> SearchResourceHistoryAsync(this IMediator mediator, PartialDateTime since = null, PartialDateTime at = null, int? count = null, string continuationToken = null, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            var result = await mediator.Send(new SearchResourceHistoryRequest(since, at, count, continuationToken), cancellationToken);

            return result.Bundle;
        }

        public static async Task<Bundle> SearchResourceHistoryAsync(this IMediator mediator, string resourceType, PartialDateTime since = null, PartialDateTime at = null, int? count = null, string continuationToken = null, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            var result = await mediator.Send(new SearchResourceHistoryRequest(resourceType, since, at, count, continuationToken), cancellationToken);

            return result.Bundle;
        }

        public static async Task<Bundle> SearchResourceHistoryAsync(this IMediator mediator, string resourceType, string resourceId, PartialDateTime since = null, PartialDateTime at = null, int? count = null, string continuationToken = null, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            var result = await mediator.Send(new SearchResourceHistoryRequest(resourceType, resourceId, since, at, count, continuationToken), cancellationToken);

            return result.Bundle;
        }

        public static async Task<Bundle> SearchResourceCompartmentAsync(this IMediator mediator, string compartmentType, string compartmentId, string resourceType, IReadOnlyList<Tuple<string, string>> queries, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            var result = await mediator.Send(new CompartmentResourceRequest(compartmentType, compartmentId, resourceType, queries), cancellationToken);

            return result.Bundle;
        }

        public static async Task<CapabilityStatement> GetCapabilitiesAsync(this IMediator mediator, bool isSystem = false, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            if (isSystem)
            {
                var sysResponse = await mediator.Send(new GetSystemCapabilitiesRequest(), cancellationToken);
                return sysResponse.CapabilityStatement;
            }

            var response = await mediator.Send(new GetCapabilitiesRequest(), cancellationToken);
            return response.CapabilityStatement;
        }

        public static async Task<CreateExportResponse> ExportAsync(
            this IMediator mediator,
            Uri requestUri,
            string destinationType,
            string destinationConnectionString,
            CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(requestUri, nameof(requestUri));
            EnsureArg.IsNotNullOrWhiteSpace(destinationType, nameof(destinationType));
            EnsureArg.IsNotNullOrWhiteSpace(destinationConnectionString, nameof(destinationConnectionString));

            var request = new CreateExportRequest(requestUri, destinationType, destinationConnectionString);

            var response = await mediator.Send(request, cancellationToken);
            return response;
        }

        public static async Task<GetExportResponse> GetExportStatusAsync(this IMediator mediator, Uri requestUri, string jobId, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(requestUri, nameof(requestUri));
            EnsureArg.IsNotNullOrWhiteSpace(jobId, nameof(jobId));

            var request = new GetExportRequest(requestUri, jobId);

            GetExportResponse response = await mediator.Send(request, cancellationToken);
            return response;
        }
    }
}
