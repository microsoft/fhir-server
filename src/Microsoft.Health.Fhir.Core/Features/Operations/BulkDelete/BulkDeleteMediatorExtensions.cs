// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete.Messages;
using Microsoft.Health.Fhir.Core.Messages.Delete;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete
{
    public static class BulkDeleteMediatorExtensions
    {
        public static async Task<CreateBulkDeleteResponse> BulkDeleteAsync(this IMediator mediator, DeleteOperation deleteOperation, string resourceType, IList<Tuple<string, string>> searchParameters, bool reportIds, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            var request = new CreateBulkDeleteRequest(deleteOperation, resourceType, searchParameters, reportIds);

            CreateBulkDeleteResponse response = await mediator.Send(request, cancellationToken);
            return response;
        }

        public static async Task<GetBulkDeleteResponse> GetBulkDeleteStatusAsync(this IMediator mediator, long jobId, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            var request = new GetBulkDeleteRequest(jobId);

            GetBulkDeleteResponse response = await mediator.Send(request, cancellationToken);
            return response;
        }

        public static async Task<CancelBulkDeleteResponse> CancelBulkDeleteAsync(this IMediator mediator, long jobId, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            var request = new CancelBulkDeleteRequest(jobId);

            return await mediator.Send(request, cancellationToken);
        }
    }
}
