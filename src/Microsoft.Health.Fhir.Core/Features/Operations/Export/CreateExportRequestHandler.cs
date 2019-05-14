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
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Export;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    public class CreateExportRequestHandler : IRequestHandler<CreateExportRequest, CreateExportResponse>
    {
        private IClaimsExtractor _claimsExtractor;
        private IFhirOperationDataStore _fhirOperationDataStore;
        private ISecretStore _secretStore;

        public CreateExportRequestHandler(IClaimsExtractor claimsExtractor, IFhirOperationDataStore fhirOperationDataStore, ISecretStore secretStore)
        {
            EnsureArg.IsNotNull(claimsExtractor, nameof(claimsExtractor));
            EnsureArg.IsNotNull(fhirOperationDataStore, nameof(fhirOperationDataStore));
            EnsureArg.IsNotNull(secretStore, nameof(secretStore));

            _claimsExtractor = claimsExtractor;
            _fhirOperationDataStore = fhirOperationDataStore;
            _secretStore = secretStore;
        }

        public async Task<CreateExportResponse> Handle(CreateExportRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            var jobRecord = new ExportJobRecord(request.RequestUri, _claimsExtractor.Extract());

            // Check to see if a matching job exists or not. If a matching job exists, we will return that instead.
            // Otherwise, we will create a new export job. This will be a best effort since the likelihood of this happen should be small.
            ExportJobOutcome outcome = await _fhirOperationDataStore.GetExportJobByHashAsync(jobRecord.Hash, cancellationToken);

            if (outcome == null)
            {
                // Store the destination secret.
                await _secretStore.SetSecretAsync(jobRecord.SecretName, request.DestinationInfo.ToJson());

                outcome = await _fhirOperationDataStore.CreateExportJobAsync(jobRecord, cancellationToken);
            }

            return new CreateExportResponse(outcome.JobRecord.Id);
        }
    }
}
