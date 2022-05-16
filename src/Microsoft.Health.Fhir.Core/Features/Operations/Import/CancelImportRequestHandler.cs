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
        private readonly IQueueClient _queueClient;
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly ILogger<CancelImportRequestHandler> _logger;

        public CancelImportRequestHandler(IQueueClient queueClient, IAuthorizationService<DataActions> authorizationService, ILogger<CancelImportRequestHandler> logger)
        {
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _queueClient = queueClient;
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

            TaskInfo taskInfo = await _queueClient.GetTaskByIdAsync((byte)QueueType.Import, request.TaskId, false, cancellationToken);

            if (taskInfo == null)
            {
                throw new ResourceNotFoundException(string.Format(Core.Resources.ImportTaskNotFound, request.TaskId));
            }

            if (taskInfo.Status == TaskManagement.TaskStatus.Completed || taskInfo.Status == TaskManagement.TaskStatus.Cancelled)
            {
                throw new OperationFailedException(Core.Resources.ImportOperationCompleted, HttpStatusCode.Conflict);
            }

            await _queueClient.CancelTaskByGroupIdAsync((byte)QueueType.Import, taskInfo.GroupId, cancellationToken);
            return new CancelImportResponse(HttpStatusCode.Accepted);
        }
    }
}
