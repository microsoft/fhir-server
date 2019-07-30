// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Messages.Export;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    public class GetExportRequestHandler : IRequestHandler<GetExportRequest, GetExportResponse>
    {
        private IFhirOperationDataStore _fhirOperationDataStore;

        public GetExportRequestHandler(IFhirOperationDataStore fhirOperationDataStore)
        {
            EnsureArg.IsNotNull(fhirOperationDataStore, nameof(fhirOperationDataStore));

            _fhirOperationDataStore = fhirOperationDataStore;
        }

        public async Task<GetExportResponse> Handle(GetExportRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            ExportJobOutcome outcome = await _fhirOperationDataStore.GetExportJobByIdAsync(request.JobId, cancellationToken);

            // We have an existing job. We will determine the response based on the status of the export operation.
            GetExportResponse exportResponse;

            if (outcome.JobRecord.Status.IsFinished())
            {
                var jobResult = new ExportJobResult(
                    outcome.JobRecord.QueuedTime,
                    outcome.JobRecord.RequestUri,
                    requiresAccessToken: false,
                    outcome.JobRecord.Output.Values.OrderBy(x => x.Type, StringComparer.Ordinal).ToList(),
                    outcome.JobRecord.Error);

                exportResponse = new GetExportResponse(HttpStatusCode.OK, jobResult);
            }
            else
            {
                exportResponse = new GetExportResponse(HttpStatusCode.Accepted);
            }

            return exportResponse;
        }
    }
}
