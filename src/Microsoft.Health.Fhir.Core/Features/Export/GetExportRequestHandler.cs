// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
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
                exportResponse = new GetExportResponse(false);
            }
            else
            {
                exportResponse = new GetExportResponse(true, result.JobStatus);
            }

            return exportResponse;
        }
    }
}
