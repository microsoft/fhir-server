// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Medino;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate.Messages;
using Microsoft.Health.Fhir.Core.Features.Resources.Patch;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate
{
    public static class BulkUpdateMediatorExtensions
    {
        public static async Task<CreateBulkUpdateResponse> BulkUpdateAsync(this IMediator mediator, string resourceType, IList<Tuple<string, string>> searchParameters, Hl7.Fhir.Model.Parameters parameters, bool isParallel, uint maxCount, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            var request = new CreateBulkUpdateRequest(resourceType, searchParameters, parameters, isParallel, maxCount);

            CreateBulkUpdateResponse response = await mediator.SendAsync(request, cancellationToken);
            return response;
        }

        public static async Task<GetBulkUpdateResponse> GetBulkUpdateStatusAsync(this IMediator mediator, long jobId, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            var request = new GetBulkUpdateRequest(jobId);

            GetBulkUpdateResponse response = await mediator.SendAsync(request, cancellationToken);
            return response;
        }

        public static async Task<CancelBulkUpdateResponse> CancelBulkUpdateAsync(this IMediator mediator, long jobId, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            var request = new CancelBulkUpdateRequest(jobId);

            return await mediator.SendAsync(request, cancellationToken);
        }
    }
}
