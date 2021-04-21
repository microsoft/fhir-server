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
using Microsoft.Health.Fhir.Core.Messages.BulkImport;
using Microsoft.Health.Fhir.TaskManagement;
using TaskStatus = Microsoft.Health.Fhir.TaskManagement.TaskStatus;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkImport
{
    public class CancelBulkImportRequestHandler : IRequestHandler<CancelBulkImportRequest, CancelBulkImportResponse>
    {
        private readonly ITaskManager _taskManager;
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly ILogger<CancelBulkImportRequestHandler> _logger;

        public CancelBulkImportRequestHandler(ITaskManager taskManager, IAuthorizationService<DataActions> authorizationService, ILogger<CancelBulkImportRequestHandler> logger)
        {
            EnsureArg.IsNotNull(taskManager, nameof(taskManager));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _taskManager = taskManager;
            _authorizationService = authorizationService;
            _logger = logger;
        }

        public async Task<CancelBulkImportResponse> Handle(CancelBulkImportRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.Import, cancellationToken) != DataActions.Import)
            {
                throw new UnauthorizedFhirActionException();
            }

            try
            {
                var taskInfo = await _taskManager.GetTaskAsync(request.TaskId, cancellationToken);

                // if the task is already canceled or completed, return conflict status
                if (taskInfo.IsCanceled || taskInfo.Status == TaskStatus.Completed)
                {
                    return new CancelBulkImportResponse(HttpStatusCode.Conflict);
                }

                _logger.LogInformation($"Attempting to cancel bulk import task {request.TaskId}");
                await _taskManager.CancelTaskAsync(request.TaskId, cancellationToken);

                return new CancelBulkImportResponse(HttpStatusCode.Accepted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to cancel bulk import task {0}", request.TaskId);
                throw;
            }
        }
    }
}
