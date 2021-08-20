﻿// -------------------------------------------------------------------------------------------------
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
    public static class ImportMediatorExtensions
    {
        public static async Task<CreateImportResponse> ImportAsync(
            this IMediator mediator,
            Uri requestUri,
            string inputFormat,
            Uri inputSource,
            IReadOnlyList<InputResource> input,
            ImportRequestStorageDetail storageDetail,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(requestUri, nameof(requestUri));

            var request = new CreateImportRequest(requestUri, inputFormat, inputSource, input, storageDetail);

            CreateImportResponse response = await mediator.Send(request, cancellationToken);
            return response;
        }

        public static async Task<GetImportResponse> GetImportStatusAsync(this IMediator mediator, string jobId, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNullOrWhiteSpace(jobId, nameof(jobId));

            var request = new GetImportRequest(jobId);

            GetImportResponse response = await mediator.Send(request, cancellationToken);
            return response;
        }

        public static async Task<CancelImportResponse> CancelImportAsync(this IMediator mediator, string jobId, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNullOrWhiteSpace(jobId, nameof(jobId));

            var request = new CancelImportRequest(jobId);

            return await mediator.Send(request, cancellationToken);
        }
    }
}
