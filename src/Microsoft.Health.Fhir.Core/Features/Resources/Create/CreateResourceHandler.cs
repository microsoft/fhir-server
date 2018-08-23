// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Upsert;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Create
{
    public class CreateResourceHandler : IRequestHandler<CreateResourceRequest, UpsertResourceResponse>
    {
        private readonly IFhirRepository _repository;

        public CreateResourceHandler(IFhirRepository repository)
        {
            EnsureArg.IsNotNull(repository, nameof(repository));

            _repository = repository;
        }

        public async Task<UpsertResourceResponse> Handle(CreateResourceRequest message, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(message, nameof(message));

            var outcome = await _repository.CreateAsync(message.Resource, cancellationToken);
            return new UpsertResourceResponse(new SaveOutcome(outcome, SaveOutcomeType.Created));
        }
    }
}
