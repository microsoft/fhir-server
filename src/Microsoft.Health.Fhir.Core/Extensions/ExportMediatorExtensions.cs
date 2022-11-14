// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Messages.Export;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class ExportMediatorExtensions
    {
        public static async Task<CreateExportResponse> ExportAsync(
            this IMediator mediator,
            Uri requestUri,
            ExportJobType requestType,
            string resourceType,
            PartialDateTime since,
            PartialDateTime till,
            string filters,
            string groupId,
            string containerName,
            string formatName,
            bool isParallel,
            string anonymizationConfigurationCollectionReference,
            string anonymizationConfigLocation,
            string anonymizationConfigFileETag,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(requestUri, nameof(requestUri));

            var request = new CreateExportRequest(requestUri, requestType, resourceType, since, till, filters, groupId, containerName, formatName, isParallel, anonymizationConfigurationCollectionReference, anonymizationConfigLocation, anonymizationConfigFileETag);

            CreateExportResponse response = await mediator.Send(request, cancellationToken);
            return response;
        }

        public static async Task<GetExportResponse> GetExportStatusAsync(this IMediator mediator, Uri requestUri, string jobId, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(requestUri, nameof(requestUri));
            EnsureArg.IsNotNullOrWhiteSpace(jobId, nameof(jobId));

            var request = new GetExportRequest(requestUri, jobId);

            GetExportResponse response = await mediator.Send(request, cancellationToken);
            return response;
        }

        public static async Task<CancelExportResponse> CancelExportAsync(this IMediator mediator, string jobId, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNullOrWhiteSpace(jobId, nameof(jobId));

            var request = new CancelExportRequest(jobId);

            return await mediator.Send(request, cancellationToken);
        }
    }
}
