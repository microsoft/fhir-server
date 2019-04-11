// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
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
                exportResponse = new GetExportResponse(jobExists: false, HttpStatusCode.NotFound);
                return exportResponse;
            }

            // We have an existing job. Let us determine the response based on the status of the
            // export operation.
            if (result.JobStatus == OperationStatus.Completed)
            {
                var jobResult = new ExportJobResult(
                    result.QueuedTime,
                    result.RequestUri,
                    requiresAccessToken: false,
                    result.Output,
                    result.Errors);

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
