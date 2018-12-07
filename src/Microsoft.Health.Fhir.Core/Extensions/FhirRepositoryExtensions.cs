// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Messages.Get;
using Microsoft.Health.Fhir.Core.Messages.Upsert;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class FhirRepositoryExtensions
    {
        public static async Task<Resource> CreateResourceAsync(this Mediator mediator, Resource resource, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(resource, nameof(resource));

            UpsertResourceResponse result = await mediator.Send<UpsertResourceResponse>(new CreateResourceRequest(resource), cancellationToken);

            return result.Outcome.Resource;
        }

        public static async Task<SaveOutcome> UpsertResourceAsync(this Mediator mediator, Resource resource, WeakETag weakETag = null, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(resource, nameof(resource));

            UpsertResourceResponse result = await mediator.Send<UpsertResourceResponse>(new UpsertResourceRequest(resource, weakETag), cancellationToken);

            return result.Outcome;
        }

        public static async Task<Resource> GetResourceAsync(this Mediator mediator, ResourceKey key, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(key, nameof(key));

            GetResourceResponse result = await mediator.Send(new GetResourceRequest(key), cancellationToken);

            return result.Resource;
        }

        public static async Task<ResourceKey> DeleteResourceAsync(this Mediator mediator, ResourceKey key, bool hardDelete, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(key, nameof(key));

            var result = await mediator.Send(new DeleteResourceRequest(key, hardDelete), cancellationToken);

            return result.ResourceKey;
        }
    }
}
