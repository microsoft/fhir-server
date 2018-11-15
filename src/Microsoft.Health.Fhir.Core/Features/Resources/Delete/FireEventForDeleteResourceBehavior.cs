// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Messages.Upsert;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Delete
{
    public class FireEventForDeleteResourceBehavior :
        IPipelineBehavior<DeleteResourceRequest, DeleteResourceResponse>
    {
        private readonly IMediator _mediator;

        public FireEventForDeleteResourceBehavior(IMediator mediator)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            _mediator = mediator;
        }

        public async Task<DeleteResourceResponse> Handle(DeleteResourceRequest request, CancellationToken cancellationToken, RequestHandlerDelegate<DeleteResourceResponse> next)
        {
            EnsureArg.IsNotNull(request, nameof(request));
            EnsureArg.IsNotNull(next, nameof(next));

            DeleteResourceResponse response = await next();

            await _mediator.Publish(new ResourceDeletedEvent(response.Key), cancellationToken);

            return response;
        }
    }
}
