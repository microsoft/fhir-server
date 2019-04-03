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
    public class CreateExportRequestHandler : IRequestHandler<CreateExportRequest, CreateExportResponse>
    {
        private IDataStore _dataStore;

        public CreateExportRequestHandler(IDataStore dataStore)
        {
            EnsureArg.IsNotNull(dataStore, nameof(dataStore));

            _dataStore = dataStore;
        }

        public async Task<CreateExportResponse> Handle(CreateExportRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            // TODO: Later we will add some logic here that will check whether a duplucate job already exists
            // and handle it accordingly. For now we just assume all export jobs are unique and create a new one.

            var jobRecord = new ExportJobRecord(request, 1);
            var responseCode = await _dataStore.UpsertExportJobAsync(jobRecord, cancellationToken);

            // Upsert returns http Created for new documents and http OK if it updated an existing document.
            // We expect the former in this scenario.
            if (responseCode == System.Net.HttpStatusCode.Created)
            {
                return new CreateExportResponse(jobRecord.Id, true);
            }
            else
            {
                return new CreateExportResponse(jobRecord.Id, false);
            }
        }
    }
}
