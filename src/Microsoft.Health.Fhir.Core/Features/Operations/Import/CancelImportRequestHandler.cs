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
using Microsoft.Health.JobManagement;

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

            JobInfo jobInfo = await _queueClient.GetJobByIdAsync((byte)QueueType.Import, request.JobId, false, cancellationToken);

            if (jobInfo == null)
            {
                throw new ResourceNotFoundException(string.Format(Core.Resources.ImportJobNotFound, request.JobId));
            }

            if (jobInfo.Status == JobManagement.JobStatus.Completed || jobInfo.Status == JobManagement.JobStatus.Cancelled || jobInfo.Status == JobManagement.JobStatus.Failed)
            {
                throw new OperationFailedException(Core.Resources.ImportOperationCompleted, HttpStatusCode.Conflict);
            }

            await _queueClient.CancelJobByGroupIdAsync((byte)QueueType.Import, jobInfo.GroupId, cancellationToken);
            return new CancelImportResponse(HttpStatusCode.Accepted);
        }
    }
}
