// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Messages.BulkImport;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class BulkImportMediatorExtensions
    {
        public static async Task<CreateBulkImportResponse> BulkImportAsync(
            this IMediator mediator,
            Uri requestUri,
            BulkImportRequestConfiguration requestConfiguration,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(requestUri, nameof(requestUri));

            var request = new CreateBulkImportRequest(requestUri, requestConfiguration);

            CreateBulkImportResponse response = await mediator.Send(request, cancellationToken);
            return response;
        }

        public static async Task<GetBulkImportResponse> GetBulkImportStatusAsync(this IMediator mediator, Uri requestUri, string jobId, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(requestUri, nameof(requestUri));
            EnsureArg.IsNotNullOrWhiteSpace(jobId, nameof(jobId));

            var request = new GetBulkImportRequest(requestUri, jobId);

            GetBulkImportResponse response = await mediator.Send(request, cancellationToken);
            return response;
        }

        public static async Task<CancelBulkImportResponse> CancelBulkImportAsync(this IMediator mediator, string jobId, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNullOrWhiteSpace(jobId, nameof(jobId));

            var request = new CancelBulkImportRequest(jobId);

            return await mediator.Send(request, cancellationToken);
        }
    }
}
