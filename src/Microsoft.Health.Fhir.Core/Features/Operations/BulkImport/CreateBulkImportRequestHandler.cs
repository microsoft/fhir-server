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
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.BulkImport;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkImport
{
    /// <summary>
    /// MediatR request handler. Called when the BulkImportController creates an BulkImport job.
    /// </summary>
    public class CreateBulkImportRequestHandler : IRequestHandler<CreateBulkImportRequest, CreateBulkImportResponse>
    {
        private readonly IClaimsExtractor _claimsExtractor;
        private readonly IFhirOperationDataStore _fhirOperationDataStore;
        private readonly IFhirAuthorizationService _authorizationService;

        public CreateBulkImportRequestHandler(
            IClaimsExtractor claimsExtractor,
            IFhirOperationDataStore fhirOperationDataStore,
            IFhirAuthorizationService authorizationService)
        {
            EnsureArg.IsNotNull(claimsExtractor, nameof(claimsExtractor));
            EnsureArg.IsNotNull(fhirOperationDataStore, nameof(fhirOperationDataStore));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));

            _claimsExtractor = claimsExtractor;
            _fhirOperationDataStore = fhirOperationDataStore;
            _authorizationService = authorizationService;
        }

        public async Task<CreateBulkImportResponse> Handle(CreateBulkImportRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.BulkImport) != DataActions.BulkImport)
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

            return new CreateBulkImportResponse("123");
        }
    }
}
