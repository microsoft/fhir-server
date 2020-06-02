// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
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
    public class GetReindexRequestHandler : IRequestHandler<GetReindexRequest, GetReindexResponse>
    {
        private readonly IFhirOperationDataStore _fhirOperationDataStore;
        private readonly IFhirAuthorizationService _authorizationService;

        public GetReindexRequestHandler(IFhirOperationDataStore fhirOperationDataStore, IFhirAuthorizationService authorizationService)
        {
            EnsureArg.IsNotNull(fhirOperationDataStore, nameof(fhirOperationDataStore));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));

            _fhirOperationDataStore = fhirOperationDataStore;
            _authorizationService = authorizationService;
        }

        public async Task<GetReindexResponse> Handle(GetReindexRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.Reindex) != DataActions.Reindex)
            {
                throw new UnauthorizedFhirActionException();
            }

            if (request.JobId != null)
            {
                return await GetSingleReindexJobAsync(request.JobId, cancellationToken);
            }
            else
            {
                return await GetListOfReindexJobs();
            }
        }

        private Task<GetReindexResponse> GetListOfReindexJobs()
        {
            // TODO: build list of reindex jobs
            throw new OperationNotImplementedException("Get list of Reindex jobs not yet implemented.");
        }

        private async Task<GetReindexResponse> GetSingleReindexJobAsync(string jobId, CancellationToken cancellationToken)
        {
            try
            {
                ReindexJobWrapper reindexJob = await _fhirOperationDataStore.GetReindexJobByIdAsync(jobId, cancellationToken);
                return new GetReindexResponse(HttpStatusCode.OK, reindexJob);
            }
            catch (Exception ex)
            {
                throw new OperationFailedException($"Unable to read reindex job with id {jobId}, error: {ex.Message}", HttpStatusCode.BadRequest);
            }
        }
    }
}
