// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Upsert;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Upsert
{
    public class FireEventForCreateOrUpdateResourceBehavior :
        IPipelineBehavior<CreateResourceRequest, UpsertResourceResponse>,
        IPipelineBehavior<UpsertResourceRequest, UpsertResourceResponse>
    {
        private readonly IMediator _mediator;

        public FireEventForCreateOrUpdateResourceBehavior(IMediator mediator)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            _mediator = mediator;
        }

        public async Task<UpsertResourceResponse> Handle(
            CreateResourceRequest request,
            CancellationToken cancellationToken,
            RequestHandlerDelegate<UpsertResourceResponse> next)
        {
            EnsureArg.IsNotNull(request, nameof(request));
            EnsureArg.IsNotNull(next, nameof(next));

            var response = await next();
            await FireEvent(response, cancellationToken);
            return response;
        }

        public async Task<UpsertResourceResponse> Handle(
            UpsertResourceRequest request,
            CancellationToken cancellationToken,
            RequestHandlerDelegate<UpsertResourceResponse> next)
        {
            EnsureArg.IsNotNull(request, nameof(request));
            EnsureArg.IsNotNull(next, nameof(next));

            var response = await next();
            await FireEvent(response, cancellationToken);
            return response;
        }

        private async Task FireEvent(UpsertResourceResponse response, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(response, nameof(response));

            await _mediator.Publish(
                new ResourceUpsertedEvent(response.Outcome.Resource, response.Outcome.Outcome),
                cancellationToken);
        }
    }
}
