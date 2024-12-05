// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
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

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class CancelImportRequestHandler : IRequestHandler<CancelImportRequest, CancelImportResponse>
    {
        private readonly IQueueClient _queueClient;
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly ILogger<CancelImportRequestHandler> _logger;

        public CancelImportRequestHandler(IQueueClient queueClient, IAuthorizationService<DataActions> authorizationService, ILogger<CancelImportRequestHandler> logger)
        {
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _queueClient = queueClient;
            _authorizationService = authorizationService;
            _logger = logger;
        }

        public async Task<CancelImportResponse> Handle(CancelImportRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.Import, cancellationToken) != DataActions.Import)
            {
                throw new UnauthorizedFhirActionException();
            }

            // We need to check the status of all jobs
            IReadOnlyList<JobInfo> jobs = await _queueClient.GetJobByGroupIdAsync(QueueType.Import, request.JobId, false, cancellationToken);

            if (jobs.Count == 0)
            {
                throw new ResourceNotFoundException(string.Format(Core.Resources.ImportJobNotFound, request.JobId));
            }

            var anyFailed = false;
            var allComplete = true;

            // Check each job status
            foreach (var job in jobs)
            {
                if (job.Status == JobStatus.Failed)
                {
                    anyFailed = true;
                    break;
                }

                if (job.Status != JobStatus.Completed)
                {
                    allComplete = false;
                    break;
                }
            }

            // If the job is already completed or failed, return conflict status.
            if (anyFailed || allComplete)
            {
                throw new OperationFailedException(Core.Resources.ImportOperationCompleted, HttpStatusCode.Conflict);
            }

            // Try to cancel the job
            _logger.LogInformation("Attempting to cancel import job {JobId}", request.JobId);
            await _queueClient.CancelJobByGroupIdAsync(QueueType.Import, request.JobId, cancellationToken);

            return new CancelImportResponse(HttpStatusCode.Accepted);
        }
    }
}
