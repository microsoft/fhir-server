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

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class GetImportRequestHandler : IRequestHandler<GetImportRequest, GetImportResponse>
    {
        private readonly ITaskManager _taskManager;
        private readonly IImportTaskDataStore _importTaskDataStore;
        private readonly IAuthorizationService<DataActions> _authorizationService;

        public GetImportRequestHandler(ITaskManager taskManager, IImportTaskDataStore importTaskDataStore, IAuthorizationService<DataActions> authorizationService)
        {
            EnsureArg.IsNotNull(taskManager, nameof(taskManager));
            EnsureArg.IsNotNull(importTaskDataStore, nameof(importTaskDataStore));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));

            _taskManager = taskManager;
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

            TaskInfo taskInfo = await _taskManager.GetTaskAsync(request.TaskId, cancellationToken);
            if (taskInfo == null)
            {
                throw new ResourceNotFoundException(string.Format(Core.Resources.ImportTaskNotFound, request.TaskId));
            }

            ImportOrchestratorTaskInputData inputData = JsonConvert.DeserializeObject<ImportOrchestratorTaskInputData>(taskInfo.InputData);

            if (taskInfo.Status != TaskManagement.TaskStatus.Completed)
            {
                if (taskInfo.IsCanceled)
                {
                    throw new OperationFailedException(Core.Resources.UserRequestedCancellation, HttpStatusCode.BadRequest);
                }

                if (inputData.StoreProgressInSubTask)
                {
                    ImportTaskResult result = new ImportTaskResult()
                    {
                        Request = inputData.RequestUri.ToString(),
                        TransactionTime = DateTimeOffset.UtcNow,
                    };
                    (List<ImportOperationOutcome> completedOperationOutcome, List<ImportFailedOperationOutcome> failedOperationOutcome)
                            = await GetProcessingResultAsync(taskInfo, cancellationToken);

                    result.Output = completedOperationOutcome;
                    result.Error = failedOperationOutcome;

                    return new GetImportResponse(HttpStatusCode.Accepted, result);
                }
                else
                {
                    return new GetImportResponse(HttpStatusCode.Accepted);
                }
            }
            else
            {
                TaskResultData resultData = JsonConvert.DeserializeObject<TaskResultData>(taskInfo.Result);
                if (resultData.Result == TaskResult.Success)
                {
                    ImportTaskResult result = JsonConvert.DeserializeObject<ImportTaskResult>(resultData.ResultData);

                    if (inputData.StoreProgressInSubTask)
                    {
                        (List<ImportOperationOutcome> completedOperationOutcome, List<ImportFailedOperationOutcome> failedOperationOutcome)
                            = await GetProcessingResultAsync(taskInfo, cancellationToken);

                        result.Output = completedOperationOutcome;
                        result.Error = failedOperationOutcome;
                    }

                    return new GetImportResponse(HttpStatusCode.OK, result);
                }
                else if (resultData.Result == TaskResult.Fail)
                {
                    ImportTaskErrorResult errorResult = JsonConvert.DeserializeObject<ImportTaskErrorResult>(resultData.ResultData);

                    string failureReason = errorResult.ErrorMessage;
                    HttpStatusCode failureStatusCode = errorResult.HttpStatusCode;

                    throw new OperationFailedException(
                        string.Format(Core.Resources.OperationFailed, OperationsConstants.Import, failureReason), failureStatusCode);
                }
                else
                {
                    throw new OperationFailedException(Core.Resources.UserRequestedCancellation, HttpStatusCode.BadRequest);
                }
            }
        }

        private async Task<(List<ImportOperationOutcome> completedOperationOutcome, List<ImportFailedOperationOutcome> failedOperationOutcome)> GetProcessingResultAsync(TaskInfo taskInfo, CancellationToken cancellationToken)
        {
            var processingResults = await _importTaskDataStore.GetImportProcessingTaskResultAsync(taskInfo.QueueId, taskInfo.TaskId, cancellationToken);
            List<ImportOperationOutcome> completedOperationOutcome = new List<ImportOperationOutcome>();
            List<ImportFailedOperationOutcome> failedOperationOutcome = new List<ImportFailedOperationOutcome>();
            foreach (var processingResult in processingResults)
            {
                TaskResultData taskResultData = JsonConvert.DeserializeObject<TaskResultData>(processingResult);
                ImportProcessingTaskResult procesingTaskResult = JsonConvert.DeserializeObject<ImportProcessingTaskResult>(taskResultData.ResultData);
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
