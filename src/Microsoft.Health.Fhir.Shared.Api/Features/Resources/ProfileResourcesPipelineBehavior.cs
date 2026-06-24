// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Medino;
using Microsoft.Health.Fhir.Core.Messages.Bundle;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Messages.Upsert;

namespace Microsoft.Health.Fhir.Api.Features.Resources
{
    public sealed class ProfileResourcesPipelineBehavior<TResourceRequest, TResponse> : IPipelineBehavior<TResourceRequest, TResponse>
        where TResourceRequest : BaseBundleInnerRequest, IRequest<TResponse>
    {
        private readonly ProfileResourcesBehaviour _innerBehavior;

        public ProfileResourcesPipelineBehavior(ProfileResourcesBehaviour innerBehavior)
        {
            EnsureArg.IsNotNull(innerBehavior, nameof(innerBehavior));

            _innerBehavior = innerBehavior;
        }

        public async Task<TResponse> HandleAsync(TResourceRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            return request switch
            {
                ConditionalUpsertResourceRequest conditionalUpsertRequest => await _innerBehavior.HandleCoreAsync(conditionalUpsertRequest.Resource.InstanceType, conditionalUpsertRequest.IsBundleInnerRequest, next, cancellationToken),
                ConditionalCreateResourceRequest conditionalCreateRequest => await _innerBehavior.HandleCoreAsync(conditionalCreateRequest.Resource.InstanceType, conditionalCreateRequest.IsBundleInnerRequest, next, cancellationToken),
                UpsertResourceRequest upsertRequest => await _innerBehavior.HandleCoreAsync(upsertRequest.Resource.InstanceType, upsertRequest.IsBundleInnerRequest, next, cancellationToken),
                CreateResourceRequest createRequest => await _innerBehavior.HandleCoreAsync(createRequest.Resource.InstanceType, createRequest.IsBundleInnerRequest, next, cancellationToken),
                DeleteResourceRequest deleteRequest => await _innerBehavior.HandleCoreAsync(deleteRequest.ResourceKey.ResourceType, deleteRequest.IsBundleInnerRequest, next, cancellationToken),
                _ => throw new InvalidOperationException($"Unsupported request type '{request.GetType().FullName}'."),
            };
        }
    }
}
