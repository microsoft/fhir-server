// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Export;
using Polly;
using Polly.Retry;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    public class CancelExportRequestHandler : IRequestHandler<CancelExportRequest, CancelExportResponse>
    {
        private const int DefaultRetryCount = 3;
        private static readonly Func<int, TimeSpan> DefaultSleepDurationProvider = new Func<int, TimeSpan>(retryCount => TimeSpan.FromSeconds(Math.Pow(2, retryCount)));

        private readonly IFhirOperationDataStore _fhirOperationDataStore;
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly ILogger<CancelExportRequestHandler> _logger;
        private readonly AsyncRetryPolicy _retryPolicy;

        public CancelExportRequestHandler(IFhirOperationDataStore fhirOperationDataStore, IAuthorizationService<DataActions> authorizationService, ILogger<CancelExportRequestHandler> logger)
            : this(fhirOperationDataStore, authorizationService, DefaultRetryCount, DefaultSleepDurationProvider, logger)
        {
        }

        public CancelExportRequestHandler(IFhirOperationDataStore fhirOperationDataStore, IAuthorizationService<DataActions> authorizationService, int retryCount, Func<int, TimeSpan> sleepDurationProvider, ILogger<CancelExportRequestHandler> logger)
        {
            EnsureArg.IsNotNull(fhirOperationDataStore, nameof(fhirOperationDataStore));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsGte(retryCount, 0, nameof(retryCount));
            EnsureArg.IsNotNull(sleepDurationProvider, nameof(sleepDurationProvider));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _fhirOperationDataStore = fhirOperationDataStore;
            _authorizationService = authorizationService;
            _logger = logger;

            _retryPolicy = Policy.Handle<JobConflictException>()
                .WaitAndRetryAsync(retryCount, sleepDurationProvider);
        }

        public async Task<CancelExportResponse> Handle(CancelExportRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.Export, cancellationToken) != DataActions.Export)
            {
                throw new UnauthorizedFhirActionException();
            }

            CancelExportResponse cancelResponse;
            try
            {
                cancelResponse = await _retryPolicy.ExecuteAsync(async () =>
                {
                    ExportJobOutcome outcome = await _fhirOperationDataStore.GetExportJobByIdAsync(request.JobId, cancellationToken);

                    if (outcome.JobRecord.GroupId != request.JobId)
                    {
                        throw new OperationFailedException(Core.Resources.CancellationOnlyOnMainJob, HttpStatusCode.BadRequest);
                    }

                    // May be not needed as already convered in GetExportJobByIdAsync.
                    // If the orchestrator job is already canceled (cancelled by user when the job status was Created or Running), we will return 404
                    if (outcome.JobRecord.Status == OperationStatus.Canceled || outcome.JobRecord.CancelRequested)
                    {
                        throw new JobNotFoundException(string.Format(Core.Resources.JobNotFound, request.JobId));
                    }
                    else if (outcome.JobRecord.Status != OperationStatus.Completed && outcome.JobRecord.Status != OperationStatus.Failed)
                    {
                        // We do not want to overwrite the status Completed and Failed status
                        // Try to cancel the job. (in case of Running)
                        outcome.JobRecord.Status = OperationStatus.Canceled;
                    }

                    outcome.JobRecord.CanceledTime = Clock.UtcNow;

                    outcome.JobRecord.FailureDetails = new JobFailureDetails(Core.Resources.UserRequestedCancellation, HttpStatusCode.NoContent);

                    _logger.LogInformation("Attempting to cancel export job {JobId}", request.JobId);
                    await _fhirOperationDataStore.UpdateExportJobAsync(outcome.JobRecord, outcome.ETag, cancellationToken);

                    return new CancelExportResponse(HttpStatusCode.Accepted);
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to cancel export job {JobId}", request.JobId);
                throw;
            }

            return cancelResponse;
        }
    }
}
