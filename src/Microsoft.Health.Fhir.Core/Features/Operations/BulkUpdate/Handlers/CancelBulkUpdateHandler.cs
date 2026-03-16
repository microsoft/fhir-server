// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate.Messages;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate.Handlers
{
    public class CancelBulkUpdateHandler : IRequestHandler<CancelBulkUpdateRequest, CancelBulkUpdateResponse>
    {
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly IQueueClient _queueClient;
        private readonly ISupportedProfilesStore _supportedProfiles;
        private readonly ILogger<CancelBulkUpdateHandler> _logger;

        public CancelBulkUpdateHandler(
            IAuthorizationService<DataActions> authorizationService,
            IQueueClient queueClient,
            ISupportedProfilesStore supportedProfiles,
            ILogger<CancelBulkUpdateHandler> logger)
        {
            _authorizationService = EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            _queueClient = EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            _supportedProfiles = EnsureArg.IsNotNull(supportedProfiles, nameof(supportedProfiles));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        public async Task<CancelBulkUpdateResponse> Handle(CancelBulkUpdateRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.BulkOperator, cancellationToken) != DataActions.BulkOperator)
            {
                throw new UnauthorizedFhirActionException();
            }

            try
            {
                var jobs = await _queueClient.GetJobByGroupIdAsync((byte)QueueType.BulkUpdate, request.JobId, false, cancellationToken);

                if (jobs == null || jobs.Count == 0)
                {
                    throw new JobNotFoundException(string.Format(Core.Resources.JobNotFound, request.JobId));
                }

                var conflict = false;
                var allComplete = true;
                var needRefresh = false;

                // Check if any completed job in the group updated a profile resource type
                var profileTypes = _supportedProfiles.GetProfilesTypes();
                var hasPendingJobs = jobs.Any(job => job.Status == JobStatus.Running || job.Status == JobStatus.Created);
                foreach (var job in jobs)
                {
                    BulkUpdateResult bulkUpdateResult;
                    bool softFailed = false;
                    try
                    {
                        bulkUpdateResult = job.DeserializeResult<BulkUpdateResult>();
                        if (bulkUpdateResult.ResourcesUpdated.Keys.Any(profileTypes.Contains) == true)
                        {
                            needRefresh = true;
                        }

                        if (job.Status == JobStatus.Failed && bulkUpdateResult.ResourcesPatchFailed.Any())
                        {
                            softFailed = true;
                        }
                    }
                    catch
                    {
                        // Do nothing
                    }

                    // If resources failed on Patch then Job will be marked as Failed and other sub jobs will still continue to run
                    // There will be conflict only if Failed job status is due to some other error than JobExecutionSoftFailureException
                    if ((!softFailed && job.Status == JobStatus.Failed) || job.Status == JobStatus.Cancelled)
                    {
                        conflict = true;
                    }

                    if (job.Status != JobStatus.Completed)
                    {
                        allComplete = false;
                    }
                }

                // If the job is already completed for any reason, return conflict status.
                // Need to return conflict when there are one/few jobs that softFailed with status as failed(causing allComplete=false), but all the other jobs are completed
                if (conflict || allComplete || !hasPendingJobs)
                {
                    throw new OperationFailedException(Core.Resources.BulkUpdateOperationCompleted, HttpStatusCode.Conflict);
                }

                // Try to cancel the job.
                _logger.LogInformation("Attempting to cancel bulk update job {JobId}", request.JobId);
                await _queueClient.CancelJobByGroupIdAsync(QueueType.BulkUpdate, request.JobId, cancellationToken);

                // Check if profile resource type was updated by any job in the group.
                if (needRefresh)
                {
                    _supportedProfiles.Refresh();
                    _logger.LogInformation("Profile resources updated by job {JobId}, refreshing supported profiles.", request.JobId);
                }

                return new CancelBulkUpdateResponse(HttpStatusCode.Accepted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to cancel bulk update job {JobId}", request.JobId);
                throw;
            }
        }
    }
}
