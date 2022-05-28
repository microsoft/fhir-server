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

            JobInfo jobInfo = await _queueClient.GetJobByIdAsync((byte)QueueType.Import, request.JobId, false, cancellationToken);
            if (jobInfo == null || jobInfo.Status == JobStatus.Archived)
            {
                throw new ResourceNotFoundException(string.Format(Core.Resources.ImportJobNotFound, request.JobId));
            }

            if (jobInfo.Status == JobStatus.Created)
            {
                return new GetImportResponse(HttpStatusCode.Accepted);
            }
            else if (jobInfo.Status == JobStatus.Running)
            {
                if (string.IsNullOrEmpty(jobInfo.Result))
                {
                    return new GetImportResponse(HttpStatusCode.Accepted);
                }

                ImportOrchestratorJobResult orchestratorJobResult = JsonConvert.DeserializeObject<ImportOrchestratorJobResult>(jobInfo.Result);

                (List<ImportOperationOutcome> completedOperationOutcome, List<ImportFailedOperationOutcome> failedOperationOutcome)
                    = await GetProcessingResultAsync(jobInfo, cancellationToken);

                ImportJobResult result = new ImportJobResult()
                {
                    Request = orchestratorJobResult.Request,
                    TransactionTime = orchestratorJobResult.TransactionTime,
                    Output = completedOperationOutcome,
                    Error = failedOperationOutcome,
                };

                return new GetImportResponse(HttpStatusCode.Accepted, result);
            }
            else if (jobInfo.Status == JobStatus.Completed)
            {
                ImportOrchestratorJobResult orchestratorJobResult = JsonConvert.DeserializeObject<ImportOrchestratorJobResult>(jobInfo.Result);

                (List<ImportOperationOutcome> completedOperationOutcome, List<ImportFailedOperationOutcome> failedOperationOutcome)
                    = await GetProcessingResultAsync(jobInfo, cancellationToken);

                ImportJobResult result = new ImportJobResult()
                {
                    Request = orchestratorJobResult.Request,
                    TransactionTime = orchestratorJobResult.TransactionTime,
                    Output = completedOperationOutcome,
                    Error = failedOperationOutcome,
                };

                return new GetImportResponse(HttpStatusCode.OK, result);
            }
            else if (jobInfo.Status == JobStatus.Failed)
            {
                ImportOrchestratorJobErrorResult errorResult = JsonConvert.DeserializeObject<ImportOrchestratorJobErrorResult>(jobInfo.Result);

                string failureReason = errorResult.ErrorMessage;
                HttpStatusCode failureStatusCode = errorResult.HttpStatusCode;

                throw new OperationFailedException(
                    string.Format(Core.Resources.OperationFailed, OperationsConstants.Import, failureReason), failureStatusCode);
            }
            else if (jobInfo.Status == JobStatus.Cancelled)
            {
                throw new OperationFailedException(Core.Resources.UserRequestedCancellation, HttpStatusCode.BadRequest);
            }
            else
            {
                throw new OperationFailedException(Core.Resources.UnknownError, HttpStatusCode.InternalServerError);
            }
        }

        private async Task<(List<ImportOperationOutcome> completedOperationOutcome, List<ImportFailedOperationOutcome> failedOperationOutcome)> GetProcessingResultAsync(JobInfo jobInfo, CancellationToken cancellationToken)
        {
            IEnumerable<JobInfo> jobs = await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Import, jobInfo.GroupId, false, cancellationToken);
            List<ImportOperationOutcome> completedOperationOutcome = new List<ImportOperationOutcome>();
            List<ImportFailedOperationOutcome> failedOperationOutcome = new List<ImportFailedOperationOutcome>();
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
