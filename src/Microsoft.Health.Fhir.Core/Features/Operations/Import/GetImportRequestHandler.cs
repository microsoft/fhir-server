// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

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
        private readonly IAuthorizationService<DataActions> _authorizationService;

        public GetImportRequestHandler(ITaskManager taskManager, IAuthorizationService<DataActions> authorizationService)
        {
            EnsureArg.IsNotNull(taskManager, nameof(taskManager));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));

            _taskManager = taskManager;
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
                throw new ResourceNotFoundException(string.Format(Resources.ImportTaskNotFound, request.TaskId));
            }

            if (taskInfo.Status != TaskManagement.TaskStatus.Completed)
            {
                if (taskInfo.IsCanceled)
                {
                    throw new OperationFailedException(Resources.UserRequestedCancellation, HttpStatusCode.BadRequest);
                }

                return new GetImportResponse(HttpStatusCode.Accepted);
            }
            else
            {
                TaskResultData resultData = JsonConvert.DeserializeObject<TaskResultData>(taskInfo.Result);
                if (resultData.Result == TaskResult.Success)
                {
                    ImportTaskResult result = JsonConvert.DeserializeObject<ImportTaskResult>(resultData.ResultData);
                    return new GetImportResponse(HttpStatusCode.OK, result);
                }
                else if (resultData.Result == TaskResult.Fail)
                {
                    ImportTaskErrorResult errorResult = JsonConvert.DeserializeObject<ImportTaskErrorResult>(resultData.ResultData);

                    string failureReason = errorResult.ErrorMessage;
                    HttpStatusCode failureStatusCode = errorResult.HttpStatusCode;

                    throw new OperationFailedException(
                        string.Format(Resources.OperationFailed, OperationsConstants.Import, failureReason), failureStatusCode);
                }
                else
                {
                    throw new OperationFailedException(Resources.UserRequestedCancellation, HttpStatusCode.BadRequest);
                }
            }
        }
    }
}
