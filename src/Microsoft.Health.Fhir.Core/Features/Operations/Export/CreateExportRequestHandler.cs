// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.SecretStore;
using Microsoft.Health.Fhir.Core.Messages.Export;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    public class CreateExportRequestHandler : IRequestHandler<CreateExportRequest, CreateExportResponse>
    {
        private IFhirOperationDataStore _fhirOperationDataStore;
        private ISecretStore _secretStore;

        public CreateExportRequestHandler(IFhirOperationDataStore fhirOperationDataStore, ISecretStore secretStore)
        {
            EnsureArg.IsNotNull(fhirOperationDataStore, nameof(fhirOperationDataStore));
            EnsureArg.IsNotNull(secretStore, nameof(secretStore));

            _fhirOperationDataStore = fhirOperationDataStore;
            _secretStore = secretStore;
        }

        public async Task<CreateExportResponse> Handle(CreateExportRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            // TODO: Later we will add some logic here that will check whether a duplicate job already exists
            // and handle it accordingly. For now we just assume all export jobs are unique and create a new one.

            var jobRecord = new ExportJobRecord(request.RequestUri);

            // Store the destination secret
            SecretWrapper result = await _secretStore.SetSecretAsync(jobRecord.SecretName, request.DestinationInfo.ToJson());

            ExportJobOutcome outcome = await _fhirOperationDataStore.CreateExportJobAsync(jobRecord, cancellationToken);

            // If job creation had failed we would have thrown an exception.
            return new CreateExportResponse(jobRecord.Id);
        }
    }
}
