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
using Microsoft.Health.Core;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
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
        private readonly IFhirAuthorizationService _authorizationService;
        private readonly AsyncRetryPolicy _retryPolicy;

        public CancelExportRequestHandler(IFhirOperationDataStore fhirOperationDataStore, IFhirAuthorizationService authorizationService)
            : this(fhirOperationDataStore, authorizationService, DefaultRetryCount, DefaultSleepDurationProvider)
        {
        }

        public CancelExportRequestHandler(IFhirOperationDataStore fhirOperationDataStore, IFhirAuthorizationService authorizationService, int retryCount, Func<int, TimeSpan> sleepDurationProvider)
        {
            EnsureArg.IsNotNull(fhirOperationDataStore, nameof(fhirOperationDataStore));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsGte(retryCount, 0, nameof(retryCount));
            EnsureArg.IsNotNull(sleepDurationProvider, nameof(sleepDurationProvider));

            _fhirOperationDataStore = fhirOperationDataStore;
            _authorizationService = authorizationService;
            _retryPolicy = Policy.Handle<JobConflictException>()
                .WaitAndRetryAsync(retryCount, sleepDurationProvider);
        }

        public async Task<CancelExportResponse> Handle(CancelExportRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.Export) != DataActions.Export)
            {
                throw new UnauthorizedFhirActionException();
            }

            return await _retryPolicy.ExecuteAsync(async () =>
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

                outcome.JobRecord.FailureDetails = new ExportJobFailureDetails(Resources.UserRequestedCancellation, HttpStatusCode.NoContent);

                await _fhirOperationDataStore.UpdateExportJobAsync(outcome.JobRecord, outcome.ETag, cancellationToken);

                return new CancelExportResponse(HttpStatusCode.Accepted);
            });
        }
    }
}
