// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Export;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    public class GetExportRequestHandler : IRequestHandler<GetExportRequest, GetExportResponse>
    {
        private IFhirDataStore _fhirDataStore;

        public GetExportRequestHandler(IFhirDataStore dataStore)
        {
            EnsureArg.IsNotNull(dataStore, nameof(dataStore));

            _fhirDataStore = dataStore;
        }

        public async Task<GetExportResponse> Handle(GetExportRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            var result = await _fhirDataStore.GetExportJobAsync(request.JobId, cancellationToken);

            GetExportResponse exportResponse;
            if (result == null)
            {
                exportResponse = new GetExportResponse(jobExists: false, HttpStatusCode.NotFound);
                return exportResponse;
            }

            // We have an existing job. We will determine the response based on the status of the export operation.
            if (result.JobRecord.Status == OperationStatus.Completed)
            {
                var jobResult = new ExportJobResult(
                    result.JobRecord.QueuedTime,
                    result.JobRecord.RequestUri,
                    requiresAccessToken: false,
                    result.JobRecord.Output,
                    result.JobRecord.Errors);

                exportResponse = new GetExportResponse(jobExists: true, HttpStatusCode.OK, jobResult);
            }
            else
            {
                exportResponse = new GetExportResponse(jobExists: true, HttpStatusCode.Accepted);
            }

            return exportResponse;
        }
    }
}
