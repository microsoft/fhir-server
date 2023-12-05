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
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Reindex;
using Polly;
using Polly.Retry;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex
{
    public class CancelReindexRequestHandler : IRequestHandler<CancelReindexRequest, CancelReindexResponse>
    {
        private const int DefaultRetryCount = 3;
        private static readonly Func<int, TimeSpan> DefaultSleepDurationProvider = new Func<int, TimeSpan>(retryCount => TimeSpan.FromSeconds(Math.Pow(2, retryCount)));

        private readonly IFhirOperationDataStore _fhirOperationDataStore;
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly AsyncRetryPolicy _retryPolicy;

        public CancelReindexRequestHandler(IFhirOperationDataStore fhirOperationDataStore, IAuthorizationService<DataActions> authorizationService)
            : this(fhirOperationDataStore, authorizationService, DefaultRetryCount, DefaultSleepDurationProvider)
        {
        }

        public CancelReindexRequestHandler(IFhirOperationDataStore fhirOperationDataStore, IAuthorizationService<DataActions> authorizationService, int retryCount, Func<int, TimeSpan> sleepDurationProvider)
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

        public async Task<CancelReindexResponse> Handle(CancelReindexRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.Reindex, cancellationToken) != DataActions.Reindex)
            {
                throw new UnauthorizedFhirActionException();
            }

            return await _retryPolicy.ExecuteAsync(async () =>
            {
                ReindexJobWrapper outcome = await _fhirOperationDataStore.GetReindexJobByIdAsync(request.JobId, cancellationToken);

                // If the job is already completed for any reason, return conflict status.
                if (outcome.JobRecord.Status.IsFinished())
                {
                    throw new RequestNotValidException(
                        string.Format(Core.Resources.ReindexJobInCompletedState, outcome.JobRecord.Id, outcome.JobRecord.Status));
                }

                // Try to cancel the job.
                outcome.JobRecord.Status = OperationStatus.Canceled;
                outcome.JobRecord.CanceledTime = Clock.UtcNow;

                await _fhirOperationDataStore.UpdateReindexJobAsync(outcome.JobRecord, outcome.ETag, cancellationToken);

                return new CancelReindexResponse(HttpStatusCode.Conflict, outcome);
            });
        }
    }
}
