// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Export;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    public class CreateExportRequestHandler : IRequestHandler<CreateExportRequest, CreateExportResponse>
    {
        private readonly IClaimsExtractor _claimsExtractor;
        private readonly IFhirOperationDataStore _fhirOperationDataStore;
        private readonly IFhirAuthorizationService _authorizationService;

        public CreateExportRequestHandler(IClaimsExtractor claimsExtractor, IFhirOperationDataStore fhirOperationDataStore, IFhirAuthorizationService authorizationService)
        {
            EnsureArg.IsNotNull(claimsExtractor, nameof(claimsExtractor));
            EnsureArg.IsNotNull(fhirOperationDataStore, nameof(fhirOperationDataStore));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));

            _claimsExtractor = claimsExtractor;
            _fhirOperationDataStore = fhirOperationDataStore;
            _authorizationService = authorizationService;
        }

        public async Task<CreateExportResponse> Handle(CreateExportRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (_authorizationService.CheckAccess(DataActions.Export) != DataActions.Export)
            {
                throw new UnauthorizedFhirActionException();
            }

            IReadOnlyCollection<KeyValuePair<string, string>> requestorClaims = _claimsExtractor.Extract()?
                .OrderBy(claim => claim.Key, StringComparer.Ordinal).ToList();

            // Compute the hash of the job.
            var hashObject = new
            {
                request.RequestUri,
                RequestorClaims = requestorClaims,
            };

            string hash = JsonConvert.SerializeObject(hashObject).ComputeHash();

            // Check to see if a matching job exists or not. If a matching job exists, we will return that instead.
            // Otherwise, we will create a new export job. This will be a best effort since the likelihood of this happen should be small.
            ExportJobOutcome outcome = await _fhirOperationDataStore.GetExportJobByHashAsync(hash, cancellationToken);

            if (outcome == null)
            {
                var jobRecord = new ExportJobRecord(request.RequestUri, request.ResourceType, hash, requestorClaims);

                outcome = await _fhirOperationDataStore.CreateExportJobAsync(jobRecord, cancellationToken);
            }

            return new CreateExportResponse(outcome.JobRecord.Id);
        }
    }
}
