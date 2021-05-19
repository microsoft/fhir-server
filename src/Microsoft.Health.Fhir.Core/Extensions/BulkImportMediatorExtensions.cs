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
using Microsoft.Health.Fhir.Core.Features.Operations.Import.Models;
using Microsoft.Health.Fhir.Core.Messages.Import;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class BulkImportMediatorExtensions
    {
        public static async Task<CreateImportResponse> BulkImportAsync(
            this IMediator mediator,
            Uri requestUri,
            string inputFormat,
            Uri inputSource,
            IReadOnlyList<InputResource> input,
            ImportRequestStorageDetail storageDetail,
            bool skipRunningImportTaskCheck,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(requestUri, nameof(requestUri));

            var request = new CreateImportRequest(requestUri, inputFormat, inputSource, input, storageDetail, skipRunningImportTaskCheck);

            CreateImportResponse response = await mediator.Send(request, cancellationToken);
            return response;
        }

        public static async Task<GetImportResponse> GetBulkImportStatusAsync(this IMediator mediator, Uri requestUri, string jobId, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(requestUri, nameof(requestUri));
            EnsureArg.IsNotNullOrWhiteSpace(jobId, nameof(jobId));

            var request = new GetImportRequest(requestUri, jobId);

            GetImportResponse response = await mediator.Send(request, cancellationToken);
            return response;
        }

        public static async Task<CancelImportResponse> CancelBulkImportAsync(this IMediator mediator, string jobId, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNullOrWhiteSpace(jobId, nameof(jobId));

            var request = new CancelImportRequest(jobId);

            return await mediator.Send(request, cancellationToken);
        }
    }
}
