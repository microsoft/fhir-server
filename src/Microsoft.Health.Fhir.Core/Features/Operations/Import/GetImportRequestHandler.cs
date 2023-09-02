// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
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

            JobInfo coordInfo = await _queueClient.GetJobByIdAsync(QueueType.Import, request.JobId, false, cancellationToken);
            if (coordInfo == null || coordInfo.Status == JobStatus.Archived)
            {
                throw new ResourceNotFoundException(string.Format(Core.Resources.ImportJobNotFound, request.JobId));
            }

            if (coordInfo.Status == JobStatus.Created)
            {
                return new GetImportResponse(HttpStatusCode.Accepted);
            }
            else if (coordInfo.Status == JobStatus.Running)
            {
                if (string.IsNullOrEmpty(coordInfo.Result))
                {
                    return new GetImportResponse(HttpStatusCode.Accepted);
                }

                ImportOrchestratorJobResult orchestratorJobResult = JsonConvert.DeserializeObject<ImportOrchestratorJobResult>(coordInfo.Result);

                (List<ImportOperationOutcome> completedOperationOutcome, List<ImportFailedOperationOutcome> failedOperationOutcome)
                    = await GetProcessingResultAsync(coordInfo.GroupId, cancellationToken);

                var result = new ImportJobResult()
                {
                    Request = orchestratorJobResult.Request,
                    TransactionTime = coordInfo.CreateDate,
                    Output = completedOperationOutcome,
                    Error = failedOperationOutcome,
                };

                return new GetImportResponse(HttpStatusCode.Accepted, result);
            }
            else if (coordInfo.Status == JobStatus.Completed)
            {
                ImportOrchestratorJobResult orchestratorJobResult = JsonConvert.DeserializeObject<ImportOrchestratorJobResult>(coordInfo.Result);

                (List<ImportOperationOutcome> completedOperationOutcome, List<ImportFailedOperationOutcome> failedOperationOutcome)
                    = await GetProcessingResultAsync(coordInfo.GroupId, cancellationToken);

                var result = new ImportJobResult()
                {
                    Request = orchestratorJobResult.Request,
                    TransactionTime = coordInfo.CreateDate,
                    Output = completedOperationOutcome,
                    Error = failedOperationOutcome,
                };

                return new GetImportResponse(HttpStatusCode.OK, result);
            }
            else if (coordInfo.Status == JobStatus.Failed)
            {
                ImportOrchestratorJobErrorResult errorResult = JsonConvert.DeserializeObject<ImportOrchestratorJobErrorResult>(coordInfo.Result);

                string failureReason = errorResult.ErrorMessage;
                HttpStatusCode failureStatusCode = errorResult.HttpStatusCode;

                throw new OperationFailedException(
                    string.Format(Core.Resources.OperationFailed, OperationsConstants.Import, failureReason), failureStatusCode);
            }
            else if (coordInfo.Status == JobStatus.Cancelled)
            {
                throw new OperationFailedException(Core.Resources.UserRequestedCancellation, HttpStatusCode.BadRequest);
            }
            else
            {
                throw new OperationFailedException(Core.Resources.UnknownError, HttpStatusCode.InternalServerError);
            }
        }

        private async Task<(List<ImportOperationOutcome> completedOperationOutcome, List<ImportFailedOperationOutcome> failedOperationOutcome)> GetProcessingResultAsync(long groupId, CancellationToken cancellationToken)
        {
            var start = Stopwatch.StartNew();
            var jobs = await _queueClient.GetJobByGroupIdAsync(QueueType.Import, groupId, true, cancellationToken);
            var duration = start.Elapsed.TotalSeconds;
            var completedOperationOutcome = new List<ImportOperationOutcome>();
            var failedOperationOutcome = new List<ImportFailedOperationOutcome>();
            foreach (var job in jobs.Where(_ => _.Id != groupId && _.Status == JobStatus.Completed)) // ignore coordinator && not completed
            {
                var definition = JsonConvert.DeserializeObject<ImportProcessingJobDefinition>(job.Definition);
                var result = JsonConvert.DeserializeObject<ImportProcessingJobResult>(job.Result);
                var succeeded = result.SucceededResources == 0 ? result.SucceedCount : result.SucceededResources; // TODO: Remove in stage 3
                var failed = result.FailedResources == 0 ? result.FailedCount : result.FailedResources; // TODO: Remove in stage 3
                completedOperationOutcome.Add(new ImportOperationOutcome() { Type = definition.ResourceType, Count = succeeded, InputUrl = new Uri(definition.ResourceLocation) });
                if (failed > 0)
                {
                    failedOperationOutcome.Add(new ImportFailedOperationOutcome() { Type = definition.ResourceType, Count = failed, InputUrl = new Uri(definition.ResourceLocation), Url = result.ErrorLogLocation });
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(duration * 10), cancellationToken); // throttle to avoid misuse.

            return (completedOperationOutcome, failedOperationOutcome);
        }
    }
}
