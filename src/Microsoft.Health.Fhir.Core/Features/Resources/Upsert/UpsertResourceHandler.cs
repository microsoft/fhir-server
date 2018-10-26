// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Upsert;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Upsert
{
    public class UpsertResourceHandler : IRequestHandler<UpsertResourceRequest, UpsertResourceResponse>
    {
        private readonly IFhirRepository _repository;

        public UpsertResourceHandler(IFhirRepository repository)
        {
            EnsureArg.IsNotNull(repository, nameof(repository));

            _repository = repository;
        }

        public async Task<UpsertResourceResponse> Handle(UpsertResourceRequest message, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(message, nameof(message));

            var outcome = await _repository.UpsertAsync(message.Resource, message.WeakETag, cancellationToken);

            return new UpsertResourceResponse(outcome);
        }
    }
}
