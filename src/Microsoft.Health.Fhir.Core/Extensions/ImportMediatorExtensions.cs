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
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
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
            ImportMode importMode,
            bool allowNegativeVersions,
            string errorContainerName,
            bool eventualConsistency,
            int processingUnitBytesToRead,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(requestUri, nameof(requestUri));

            var request = new CreateImportRequest(requestUri, inputFormat, inputSource, input, storageDetail, importMode, allowNegativeVersions, errorContainerName, eventualConsistency, processingUnitBytesToRead);

            CreateImportResponse response = await mediator.SendAsync(request, cancellationToken);
            return response;
        }

        public static async Task<GetImportResponse> GetImportStatusAsync(this IMediator mediator, long jobId, CancellationToken cancellationToken, bool returnDetails = false)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            var request = new GetImportRequest(jobId, returnDetails);

            GetImportResponse response = await mediator.SendAsync(request, cancellationToken);
            return response;
        }

        public static async Task<CancelImportResponse> CancelImportAsync(this IMediator mediator, long jobId, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            var request = new CancelImportRequest(jobId);

            return await mediator.SendAsync(request, cancellationToken);
        }
    }
}
