// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Export;

namespace Microsoft.Health.Fhir.Core.Features.Export
{
    public class GetExportRequestHandler : IRequestHandler<GetExportRequest, GetExportResponse>
    {
        private IDataStore _dataStore;

        public GetExportRequestHandler(IDataStore dataStore)
        {
            EnsureArg.IsNotNull(dataStore, nameof(dataStore));

            _dataStore = dataStore;
        }

        public async Task<GetExportResponse> Handle(GetExportRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            var result = await _dataStore.GetExportJobAsync(request.JobId);

            GetExportResponse exportResponse;
            if (result == null)
            {
                exportResponse = new GetExportResponse(false, HttpStatusCode.NotFound);
                return exportResponse;
            }

            // We have an existing job. Let us determine the response based on the status of the
            // export operation.
            if (result.JobStatus == OperationStatus.Completed)
            {
                var jobResult = new ExportJobResult(
                    new Instant(result.QueuedTime),
                    result.Request.RequestUri,
                    false /* requiresAccessToken */,
                    result.Output,
                    result.Errors);

                exportResponse = new GetExportResponse(true, HttpStatusCode.OK, jobResult);
            }
            else
            {
                exportResponse = new GetExportResponse(true, HttpStatusCode.Accepted);
            }

            return exportResponse;
        }
    }
}
