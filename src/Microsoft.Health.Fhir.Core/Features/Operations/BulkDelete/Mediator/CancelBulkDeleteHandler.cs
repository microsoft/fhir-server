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
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete.Mediator
{
    public class CancelBulkDeleteHandler : IRequestHandler<CancelBulkDeleteRequest, CancelBulkDeleteResponse>
    {
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly IQueueClient _queueClient;
        private readonly ILogger<CancelBulkDeleteHandler> _logger;

        public CancelBulkDeleteHandler(
            IAuthorizationService<DataActions> authorizationService,
            IQueueClient queueClient,
            ILogger<CancelBulkDeleteHandler> logger)
        {
            _authorizationService = EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            _queueClient = EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        public async Task<CancelBulkDeleteResponse> Handle(CancelBulkDeleteRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.Delete, cancellationToken) != DataActions.Delete)
            {
                throw new UnauthorizedFhirActionException();
            }

            try
            {
                var jobs = await _queueClient.GetJobByGroupIdAsync((byte)QueueType.BulkDelete, request.JobId, false, cancellationToken);

                var conflict = false;
                foreach (var job in jobs)
                {
                    if (job.Status == JobStatus.Failed || job.Status == JobStatus.Completed || job.Status == JobStatus.Cancelled)
                    {
                        conflict = true;
                    }
                }

                // If the job is already completed for any reason, return conflict status.
                if (conflict)
                {
                    return new CancelBulkDeleteResponse(HttpStatusCode.Conflict);
                }

                // Try to cancel the job.
                _logger.LogInformation("Attempting to cancel bulk delete job {JobId}", request.JobId);
                await _queueClient.CancelJobByGroupIdAsync((byte)QueueType.BulkDelete, request.JobId, cancellationToken);

                return new CancelBulkDeleteResponse(HttpStatusCode.Accepted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to cancel bulk delete job {JobId}", request.JobId);
                throw;
            }
        }
    }
}
