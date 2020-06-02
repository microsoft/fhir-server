// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Reindex;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex
{
    public class CreateReindexRequestHandler : IRequestHandler<CreateReindexRequest, CreateReindexResponse>
    {
        private readonly IClaimsExtractor _claimsExtractor;
        private readonly IFhirOperationDataStore _fhirOperationDataStore;
        private readonly IFhirAuthorizationService _authorizationService;

        public CreateReindexRequestHandler(IClaimsExtractor claimsExtractor, IFhirOperationDataStore fhirOperationDataStore, IFhirAuthorizationService authorizationService)
        {
            EnsureArg.IsNotNull(claimsExtractor, nameof(claimsExtractor));
            EnsureArg.IsNotNull(fhirOperationDataStore, nameof(fhirOperationDataStore));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));

            _claimsExtractor = claimsExtractor;
            _fhirOperationDataStore = fhirOperationDataStore;
            _authorizationService = authorizationService;
        }

        public async Task<CreateReindexResponse> Handle(CreateReindexRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.Reindex) != DataActions.Reindex)
            {
                throw new UnauthorizedFhirActionException();
            }

            if (await _fhirOperationDataStore.CheckActiveReindexJobsAsync(cancellationToken))
            {
                throw new JobConflictException(Resources.OnlyOneResourceJobAllowed);
            }

            // TODO: determine new parameters to index

            // TODO: Get hash from parameters file
            string hash = "hash";

            var jobRecord = new ReindexJobRecord(hash);
            var outcome = await _fhirOperationDataStore.CreateReindexJobAsync(jobRecord, cancellationToken);

            return new CreateReindexResponse(outcome);
        }
    }
}
