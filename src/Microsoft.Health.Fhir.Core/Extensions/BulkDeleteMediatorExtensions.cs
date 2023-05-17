// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete.Mediator;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class BulkDeleteMediatorExtensions
    {
        public static async Task<CreateBulkDeleteResponse> BulkDeleteAsync(this IMediator mediator, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            var request = new CreateBulkDeleteRequest();

            CreateBulkDeleteResponse response = await mediator.Send(request, cancellationToken);
            return response;
        }

        public static async Task<GetBulkDeleteResponse> GetBulkDeleteStatusAsync(this IMediator mediator, string jobId, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNullOrWhiteSpace(jobId, nameof(jobId));

            var request = new GetBulkDeleteRequest(jobId);

            GetBulkDeleteResponse response = await mediator.Send(request, cancellationToken);
            return response;
        }

        public static async Task<CancelBulkDeleteResponse> CancelBulkDeleteAsync(this IMediator mediator, string jobId, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNullOrWhiteSpace(jobId, nameof(jobId));

            var request = new CancelBulkDeleteRequest(jobId);

            return await mediator.Send(request, cancellationToken);
        }
    }
}
