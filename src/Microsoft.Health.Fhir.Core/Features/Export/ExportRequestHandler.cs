// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Export;

namespace Microsoft.Health.Fhir.Core.Features.Export
{
    public class ExportRequestHandler : IRequestHandler<ExportRequest, ExportResponse>
    {
        private IDataStore _dataStore;

        public ExportRequestHandler(IDataStore dataStore)
        {
            EnsureArg.IsNotNull(dataStore, nameof(dataStore));

            _dataStore = dataStore;
        }

        public async Task<ExportResponse> Handle(ExportRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            var jobRecord = new ExportJobRecord(request);
            jobRecord.Output.AddError(new ExportJobOutputComponent("error", 1, 1, 12345));
            jobRecord.Output.AddResult(new ExportJobOutputComponent("Patient", 2, 100, 13452345));

            var result = await _dataStore.UpsertExportJobAsync(jobRecord, cancellationToken);

            return new ExportResponse(jobRecord.Id, result);
        }
    }
}
