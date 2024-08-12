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
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Import;
using Microsoft.Health.JobManagement;
using Polly;
using Polly.Retry;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class CancelImportRequestHandler : IRequestHandler<CancelImportRequest, CancelImportResponse>
    {
        private const int DefaultRetryCount = 3;
        private static readonly Func<int, TimeSpan> DefaultSleepDurationProvider = new Func<int, TimeSpan>(retryCount => TimeSpan.FromSeconds(Math.Pow(2, retryCount)));

        private readonly IQueueClient _queueClient;
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly ILogger<CancelImportRequestHandler> _logger;
        private readonly AsyncRetryPolicy _retryPolicy;

        public CancelImportRequestHandler(IQueueClient queueClient, IAuthorizationService<DataActions> authorizationService, ILogger<CancelImportRequestHandler> logger)
            : this(queueClient, authorizationService, DefaultRetryCount, DefaultSleepDurationProvider, logger)
        {
        }

        public CancelImportRequestHandler(IQueueClient queueClient, IAuthorizationService<DataActions> authorizationService, int retryCount, Func<int, TimeSpan> sleepDurationProvider, ILogger<CancelImportRequestHandler> logger)
        {
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _queueClient = queueClient;
            _authorizationService = authorizationService;
            _logger = logger;

            _retryPolicy = Policy.Handle<JobConflictException>()
                .WaitAndRetryAsync(retryCount, sleepDurationProvider);
        }

        public async Task<CancelImportResponse> Handle(CancelImportRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.Import, cancellationToken) != DataActions.Import)
            {
                throw new UnauthorizedFhirActionException();
            }

            CancelImportResponse cancelResponse;

            try
            {
                cancelResponse = await _retryPolicy.ExecuteAsync(async () =>
                {
                    ExportJobOutcome outcome = await _fhirOperationDataStore.GetExportJobByIdAsync(request.JobId, cancellationToken);

                    // If the job is already completed for any reason, return conflict status.
                    if (outcome.JobRecord.Status.IsFinished())
                    {
                        return new CancelExportResponse(HttpStatusCode.Conflict);
                    }

                    // Try to cancel the job.
                    outcome.JobRecord.Status = OperationStatus.Canceled;
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
