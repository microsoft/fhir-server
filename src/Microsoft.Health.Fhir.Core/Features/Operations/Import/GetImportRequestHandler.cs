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
using Microsoft.Health.TaskManagement;
using Newtonsoft.Json;
using TaskStatus = Microsoft.Health.TaskManagement.TaskStatus;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class GetImportRequestHandler : IRequestHandler<GetImportRequest, GetImportResponse>
    {
        private readonly IQueueClient _queueClient;
        private readonly IImportTaskDataStore _importTaskDataStore;
        private readonly IAuthorizationService<DataActions> _authorizationService;

        public GetImportRequestHandler(IQueueClient queueClient, IImportTaskDataStore importTaskDataStore, IAuthorizationService<DataActions> authorizationService)
        {
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            EnsureArg.IsNotNull(importTaskDataStore, nameof(importTaskDataStore));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));

            _queueClient = queueClient;
            _importTaskDataStore = importTaskDataStore;
            _authorizationService = authorizationService;
        }

        public async Task<GetImportResponse> Handle(GetImportRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.Import, cancellationToken) != DataActions.Import)
            {
                throw new UnauthorizedFhirActionException();
            }

            TaskInfo taskInfo = await _queueClient.GetTaskByIdAsync(ImportConstants.ImportQueueType, request.TaskId, false, cancellationToken);
            if (taskInfo == null || taskInfo.Status == TaskStatus.Archived)
            {
                throw new ResourceNotFoundException(string.Format(Core.Resources.ImportTaskNotFound, request.TaskId));
            }

            if (taskInfo.Status == TaskStatus.Created)
            {
                return new GetImportResponse(HttpStatusCode.Accepted);
            }
            else if (taskInfo.Status == TaskStatus.Running)
            {
                if (string.IsNullOrEmpty(taskInfo.Result))
                {
                    return new GetImportResponse(HttpStatusCode.Accepted);
                }

                ImportOrchestratorTaskResult orchestratorTaskresult = JsonConvert.DeserializeObject<ImportOrchestratorTaskResult>(taskInfo.Result);

                (List<ImportOperationOutcome> completedOperationOutcome, List<ImportFailedOperationOutcome> failedOperationOutcome)
                    = await GetProcessingResultAsync(taskInfo, cancellationToken);

                ImportTaskResult result = new ImportTaskResult()
                {
                    Request = orchestratorTaskresult.Request,
                    TransactionTime = orchestratorTaskresult.TransactionTime,
                    Output = completedOperationOutcome,
                    Error = failedOperationOutcome,
                };

                return new GetImportResponse(HttpStatusCode.OK, result);
            }
            else if (taskInfo.Status == TaskStatus.Completed)
            {
                ImportOrchestratorTaskResult orchestratorTaskresult = JsonConvert.DeserializeObject<ImportOrchestratorTaskResult>(taskInfo.Result);

                (List<ImportOperationOutcome> completedOperationOutcome, List<ImportFailedOperationOutcome> failedOperationOutcome)
                    = await GetProcessingResultAsync(taskInfo, cancellationToken);

                ImportTaskResult result = new ImportTaskResult()
                {
                    Request = orchestratorTaskresult.Request,
                    TransactionTime = orchestratorTaskresult.TransactionTime,
                    Output = completedOperationOutcome,
                    Error = failedOperationOutcome,
                };

                return new GetImportResponse(HttpStatusCode.OK, result);
            }
            else if (taskInfo.Status == TaskStatus.Failed)
            {
                ImportOrchestratorTaskErrorResult errorResult = JsonConvert.DeserializeObject<ImportOrchestratorTaskErrorResult>(taskInfo.Result);

                string failureReason = errorResult.ErrorMessage;
                HttpStatusCode failureStatusCode = errorResult.HttpStatusCode;

                throw new OperationFailedException(
                    string.Format(Core.Resources.OperationFailed, OperationsConstants.Import, failureReason), failureStatusCode);
            }
            else if (taskInfo.Status == TaskStatus.Cancelled)
            {
                throw new OperationFailedException(Core.Resources.UserRequestedCancellation, HttpStatusCode.BadRequest);
            }
            else
            {
                throw new OperationFailedException(Core.Resources.UnknownError, HttpStatusCode.InternalServerError);
            }
        }

        private async Task<(List<ImportOperationOutcome> completedOperationOutcome, List<ImportFailedOperationOutcome> failedOperationOutcome)> GetProcessingResultAsync(TaskInfo taskInfo, CancellationToken cancellationToken)
        {
            IEnumerable<TaskInfo> tasks = await _queueClient.GetTaskByGroupIdAsync(ImportConstants.ImportQueueType, taskInfo.GroupId, false, cancellationToken);
            List<ImportOperationOutcome> completedOperationOutcome = new List<ImportOperationOutcome>();
            List<ImportFailedOperationOutcome> failedOperationOutcome = new List<ImportFailedOperationOutcome>();
            foreach (var task in tasks)
            {
                if (task.Status != TaskStatus.Completed || string.IsNullOrEmpty(task.Result))
                {
                    continue;
                }

                ImportProcessingTaskResult procesingTaskResult = JsonConvert.DeserializeObject<ImportProcessingTaskResult>(task.Result);
                if (string.IsNullOrEmpty(procesingTaskResult.ResourceLocation))
                {
                    continue;
                }

                completedOperationOutcome.Add(new ImportOperationOutcome() { Type = procesingTaskResult.ResourceType, Count = procesingTaskResult.SucceedCount, InputUrl = new Uri(procesingTaskResult.ResourceLocation) });
                if (procesingTaskResult.FailedCount > 0)
                {
                    failedOperationOutcome.Add(new ImportFailedOperationOutcome() { Type = procesingTaskResult.ResourceType, Count = procesingTaskResult.FailedCount, InputUrl = new Uri(procesingTaskResult.ResourceLocation), Url = procesingTaskResult.ErrorLogLocation });
                }
            }

            return (completedOperationOutcome, failedOperationOutcome);
        }
    }
}
