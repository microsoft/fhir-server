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
using Microsoft.Health.Fhir.Core.Features.TaskManagement;
using Microsoft.Health.Fhir.Core.Messages.BulkImport;
using Polly;
using Polly.Retry;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkImport
{
    public class CancelBulkImportRequestHandler : IRequestHandler<CancelBulkImportRequest, CancelBulkImportResponse>
    {
        private const int DefaultRetryCount = 3;
        private static readonly Func<int, TimeSpan> DefaultSleepDurationProvider = new Func<int, TimeSpan>(retryCount => TimeSpan.FromSeconds(Math.Pow(2, retryCount)));

        private readonly ITaskManager _taskManager;
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly AsyncRetryPolicy _retryPolicy;
        private readonly ILogger<CancelBulkImportRequestHandler> _logger;

        public CancelBulkImportRequestHandler(ITaskManager taskManager, IAuthorizationService<DataActions> authorizationService, ILogger<CancelBulkImportRequestHandler> logger)
            : this(taskManager, authorizationService, DefaultRetryCount, DefaultSleepDurationProvider, logger)
        {
        }

        public CancelBulkImportRequestHandler(ITaskManager taskManager, IAuthorizationService<DataActions> authorizationService, int retryCount, Func<int, TimeSpan> sleepDurationProvider, ILogger<CancelBulkImportRequestHandler> logger)
        {
            EnsureArg.IsNotNull(taskManager, nameof(taskManager));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsGte(retryCount, 0, nameof(retryCount));
            EnsureArg.IsNotNull(sleepDurationProvider, nameof(sleepDurationProvider));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _taskManager = taskManager;
            _authorizationService = authorizationService;
            _logger = logger;

            _retryPolicy = Policy.Handle<JobConflictException>()
                .WaitAndRetryAsync(retryCount, sleepDurationProvider);
        }

        public async Task<CancelBulkImportResponse> Handle(CancelBulkImportRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.Import, cancellationToken) != DataActions.Import)
            {
                throw new UnauthorizedFhirActionException();
            }

            CancelBulkImportResponse cancelResponse;
            try
            {
                var taskInfo = await _taskManager.GetTaskAsync(request.TaskId, cancellationToken);

                // if the task is already canceled or completed, return conflict status
                if (taskInfo.IsCanceled || taskInfo.Status == TaskManagement.TaskStatus.Completed)
                {
                    return new CancelBulkImportResponse(HttpStatusCode.Conflict);
                }

                cancelResponse = await _retryPolicy.ExecuteAsync(async () =>
                {
                    _logger.LogInformation($"Attempting to cancel bulk import task {request.TaskId}");
                    await _taskManager.CancelTaskAsync(request.TaskId, cancellationToken);
                    return new CancelBulkImportResponse(HttpStatusCode.Accepted);
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Unable to cancel bulk import task {request.TaskId}");
                throw;
            }

            return cancelResponse;
        }
    }
}
