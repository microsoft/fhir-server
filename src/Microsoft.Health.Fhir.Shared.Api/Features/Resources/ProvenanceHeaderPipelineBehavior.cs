// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Medino;
using Microsoft.Health.Fhir.Core.Messages.Bundle;
using Microsoft.Health.Fhir.Core.Messages.Upsert;

namespace Microsoft.Health.Fhir.Api.Features.Resources
{
    public sealed class ProvenanceHeaderPipelineBehavior<TResourceRequest> : IPipelineBehavior<TResourceRequest, UpsertResourceResponse>
        where TResourceRequest : BaseBundleInnerRequest, IRequest<UpsertResourceResponse>
    {
        private readonly ProvenanceHeaderBehavior _innerBehavior;

        public ProvenanceHeaderPipelineBehavior(ProvenanceHeaderBehavior innerBehavior)
        {
            EnsureArg.IsNotNull(innerBehavior, nameof(innerBehavior));

            _innerBehavior = innerBehavior;
        }

        public async Task<UpsertResourceResponse> HandleAsync(TResourceRequest request, RequestHandlerDelegate<UpsertResourceResponse> next, CancellationToken cancellationToken)
            => await _innerBehavior.HandleCoreAsync(next, cancellationToken);
    }
}
