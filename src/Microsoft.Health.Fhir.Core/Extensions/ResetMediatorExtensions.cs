// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Messages.Reset;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class ResetMediatorExtensions
    {
        public static async Task<CreateResetResponse> ResetAsync(
            this IMediator mediator,
            Uri requestUri,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(requestUri, nameof(requestUri));

            var request = new CreateResetRequest(requestUri);

            CreateResetResponse response = await mediator.Send(request, cancellationToken);
            return response;
        }

        public static async Task<GetResetResponse> GetResetStatusAsync(this IMediator mediator, Uri requestUri, string jobId, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(requestUri, nameof(requestUri));
            EnsureArg.IsNotNullOrWhiteSpace(jobId, nameof(jobId));

            var request = new GetResetRequest(requestUri, jobId);

            GetResetResponse response = await mediator.Send(request, cancellationToken);
            return response;
        }
    }
}
