﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Reindex;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex
{
    public class GetReindexRequestHandler : IRequestHandler<GetReindexRequest, GetReindexResponse>
    {
        private readonly IFhirOperationDataStore _fhirOperationDataStore;
        private readonly IAuthorizationService<DataActions> _authorizationService;

        public GetReindexRequestHandler(IFhirOperationDataStore fhirOperationDataStore, IAuthorizationService<DataActions> authorizationService)
        {
            EnsureArg.IsNotNull(fhirOperationDataStore, nameof(fhirOperationDataStore));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));

            _fhirOperationDataStore = fhirOperationDataStore;
            _authorizationService = authorizationService;
        }

        public async Task<GetReindexResponse> Handle(GetReindexRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            // check if the user has the required permissions (either the Reindex permission or Bulk operator permission) to get the reindex job
            DataActions requiredDataActions = DataActions.BulkOperator | DataActions.Reindex;
            var grantedActions = await _authorizationService.CheckAccess(requiredDataActions, cancellationToken);
            if ((grantedActions & requiredDataActions) == 0)
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

        private static Task<GetReindexResponse> GetListOfReindexJobs()
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
            catch (JobNotFoundException)
            {
                throw;
            }
            catch (Exception ex) when (ex.IsRequestRateExceeded())
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new OperationFailedException($"Unable to read reindex job with id {jobId}, error: {ex.Message}", HttpStatusCode.BadRequest);
            }
        }
    }
}
