// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Get;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Get
{
    public class GetResourceHandler : IRequestHandler<GetResourceRequest, GetResourceResponse>
    {
        private readonly IFhirRepository _repository;

        public GetResourceHandler(IFhirRepository repository)
        {
            EnsureArg.IsNotNull(repository, nameof(repository));

            _repository = repository;
        }

        public async Task<GetResourceResponse> Handle(GetResourceRequest message, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(message, nameof(message));

            var response = await _repository.GetAsync(message.ResourceKey, cancellationToken);

            return new GetResourceResponse(response);
        }
    }
}
