// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

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
using Microsoft.Health.TaskManagement;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class CancelImportRequestHandler : IRequestHandler<CancelImportRequest, CancelImportResponse>
    {
        private readonly ITaskManager _taskManager;
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly ILogger<CancelImportRequestHandler> _logger;

        public CancelImportRequestHandler(ITaskManager taskManager, IAuthorizationService<DataActions> authorizationService, ILogger<CancelImportRequestHandler> logger)
        {
            EnsureArg.IsNotNull(taskManager, nameof(taskManager));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _taskManager = taskManager;
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

            try
            {
                TaskInfo taskInfo = await _taskManager.CancelTaskAsync(request.TaskId, cancellationToken);

                if (taskInfo.Status == TaskManagement.TaskStatus.Completed)
                {
                    throw new OperationFailedException(Resources.ImportOperationCompleted, HttpStatusCode.Conflict);
                }
                else
                {
                    return new CancelImportResponse(HttpStatusCode.Accepted);
                }
            }
            catch (TaskNotExistException)
            {
                throw new ResourceNotFoundException(string.Format(Resources.ImportTaskNotFound, request.TaskId));
            }
        }
    }
}
