// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Import;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;
using JobStatus = Microsoft.Health.JobManagement.JobStatus;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class GetImportRequestHandler : IRequestHandler<GetImportRequest, GetImportResponse>
    {
        private readonly IQueueClient _queueClient;
        private readonly IAuthorizationService<DataActions> _authorizationService;

        public GetImportRequestHandler(IQueueClient queueClient, IAuthorizationService<DataActions> authorizationService)
        {
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));

            _queueClient = queueClient;
            _authorizationService = authorizationService;
        }

        public async Task<GetImportResponse> Handle(GetImportRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.Import, cancellationToken) != DataActions.Import)
            {
                throw new UnauthorizedFhirActionException();
            }

            JobInfo coord = await _queueClient.GetJobByIdAsync((byte)QueueType.Import, request.JobId, false, cancellationToken);
            if (coord == null || coord.Status == JobStatus.Archived)
            {
                throw new ResourceNotFoundException(string.Format(Core.Resources.ImportJobNotFound, request.JobId));
            }

            if (coord.Status == JobStatus.Created)
            {
                return new GetImportResponse(HttpStatusCode.Accepted);
            }
            else if (coord.Status == JobStatus.Failed)
            {
                var errorResult = JsonConvert.DeserializeObject<ImportOrchestratorJobErrorResult>(coord.Result);
                throw new OperationFailedException(string.Format(Core.Resources.OperationFailed, OperationsConstants.Import, errorResult.ErrorMessage), errorResult.HttpStatusCode);
            }
            else if (coord.Status == JobStatus.Cancelled)
            {
                throw new OperationFailedException(Core.Resources.UserRequestedCancellation, HttpStatusCode.BadRequest);
            }
            else if (coord.Status == JobStatus.Completed)
            {
                var groupJobs = (await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Import, coord.GroupId, false, cancellationToken)).ToList();
                var inFlightJobsExist = groupJobs.Where(_ => _.Id != coord.Id).Any(_ => _.Status == JobStatus.Running || _.Status == JobStatus.Created);
                var cancelledJobsExist = groupJobs.Where(_ => _.Id != coord.Id).Any(_ => _.Status == JobStatus.Cancelled || (_.Status == JobStatus.Running && _.CancelRequested));
                var failedJobsExist = groupJobs.Where(_ => _.Id != coord.Id).Any(_ => _.Status == JobStatus.Failed);

                if (cancelledJobsExist && !failedJobsExist) // canceled
                {
                    throw new OperationFailedException(Core.Resources.UserRequestedCancellation, HttpStatusCode.BadRequest);
                }
                else if (failedJobsExist) // processing failed
                {
                    ImportOrchestratorJobErrorResult errorResult = JsonConvert.DeserializeObject<ImportOrchestratorJobErrorResult>(coord.Result);
                    string failureReason = errorResult.ErrorMessage;
                    HttpStatusCode failureStatusCode = errorResult.HttpStatusCode;
                    throw new OperationFailedException(string.Format(Core.Resources.OperationFailed, OperationsConstants.Import, failureReason), failureStatusCode);
                }
                else if (!inFlightJobsExist) // success
                {
                    ImportOrchestratorJobResult orchestratorJobResult = JsonConvert.DeserializeObject<ImportOrchestratorJobResult>(coord.Result);

                    (List<ImportOperationOutcome> completedOperationOutcome, List<ImportFailedOperationOutcome> failedOperationOutcome)
                        = await GetProcessingResultAsync(coord.GroupId, cancellationToken);

                    var result = new ImportJobResult()
                    {
                        Request = orchestratorJobResult.Request,
                        TransactionTime = orchestratorJobResult.TransactionTime,
                        Output = completedOperationOutcome,
                        Error = failedOperationOutcome,
                    };

                    return new GetImportResponse(HttpStatusCode.OK, result);
                }
                else // running
                {
                    if (string.IsNullOrEmpty(coord.Result))
                    {
                        return new GetImportResponse(HttpStatusCode.Accepted);
                    }

                    ImportOrchestratorJobResult orchestratorJobResult = JsonConvert.DeserializeObject<ImportOrchestratorJobResult>(coord.Result);

                    (List<ImportOperationOutcome> completedOperationOutcome, List<ImportFailedOperationOutcome> failedOperationOutcome)
                        = await GetProcessingResultAsync(coord.GroupId, cancellationToken);

                    var result = new ImportJobResult()
                    {
                        Request = orchestratorJobResult.Request,
                        TransactionTime = orchestratorJobResult.TransactionTime,
                        Output = completedOperationOutcome,
                        Error = failedOperationOutcome,
                    };

                    return new GetImportResponse(HttpStatusCode.Accepted, result);
                }
            }
            else // coord is still running
            {
                return new GetImportResponse(HttpStatusCode.Accepted);
            }
        }

        private async Task<(List<ImportOperationOutcome> completedOperationOutcome, List<ImportFailedOperationOutcome> failedOperationOutcome)> GetProcessingResultAsync(long groupId, CancellationToken cancellationToken)
        {
            IEnumerable<JobInfo> jobs = await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Import, groupId, false, cancellationToken);
            var completedOperationOutcome = new List<ImportOperationOutcome>();
            var failedOperationOutcome = new List<ImportFailedOperationOutcome>();
            foreach (var job in jobs)
            {
                if (job.Status != JobStatus.Completed || string.IsNullOrEmpty(job.Result))
                {
                    continue;
                }

                ImportProcessingJobResult procesingJobResult = JsonConvert.DeserializeObject<ImportProcessingJobResult>(job.Result);
                if (string.IsNullOrEmpty(procesingJobResult.ResourceLocation))
                {
                    continue;
                }

                completedOperationOutcome.Add(new ImportOperationOutcome() { Type = procesingJobResult.ResourceType, Count = procesingJobResult.SucceedCount, InputUrl = new Uri(procesingJobResult.ResourceLocation) });
                if (procesingJobResult.FailedCount > 0)
                {
                    failedOperationOutcome.Add(new ImportFailedOperationOutcome() { Type = procesingJobResult.ResourceType, Count = procesingJobResult.FailedCount, InputUrl = new Uri(procesingJobResult.ResourceLocation), Url = procesingJobResult.ErrorLogLocation });
                }
            }

            return (completedOperationOutcome, failedOperationOutcome);
        }
    }
}
