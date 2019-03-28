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
    public class ExportRequestHandler : IRequestHandler<CreateExportRequest, CreateExportResponse>
    {
        private IDataStore _dataStore;

        public ExportRequestHandler(IDataStore dataStore)
        {
            EnsureArg.IsNotNull(dataStore, nameof(dataStore));

            _dataStore = dataStore;
        }

        public async Task<CreateExportResponse> Handle(CreateExportRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            var jobRecord = new ExportJobRecord(request, 1);
            var jobCreationResult = await _dataStore.UpsertExportJobAsync(jobRecord, cancellationToken);

            return new CreateExportResponse(jobRecord.Id, jobCreationResult);
        }
    }
}
