// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Delete;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Delete
{
    public class DeleteResourceHandler : IRequestHandler<DeleteResourceRequest, DeleteResourceResponse>
    {
        private readonly IFhirRepository _repository;

        public DeleteResourceHandler(IFhirRepository repository)
        {
            EnsureArg.IsNotNull(repository, nameof(repository));

            _repository = repository;
        }

        public async Task<DeleteResourceResponse> Handle(DeleteResourceRequest message, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(message, nameof(message));

            var deletedResourceKey = await _repository.DeleteAsync(message.ResourceKey, message.HardDelete, cancellationToken);

            if (string.IsNullOrWhiteSpace(deletedResourceKey.VersionId))
            {
                return new DeleteResourceResponse();
            }

            return new DeleteResourceResponse(WeakETag.FromVersionId(deletedResourceKey.VersionId));
        }
    }
}
