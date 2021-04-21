// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Core.Features.Security;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.BulkImport;
using Microsoft.Health.Fhir.TaskManagement;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkImport
{
    /// <summary>
    /// MediatR request handler. Called when the BulkImportController creates an BulkImport job.
    /// </summary>
    public class CreateBulkImportRequestHandler : IRequestHandler<CreateBulkImportRequest, CreateBulkImportResponse>
    {
        private readonly IClaimsExtractor _claimsExtractor;
        private readonly ITaskManager _taskManager;
        private readonly ILogger<CreateBulkImportRequestHandler> _logger;
        private readonly IAuthorizationService<DataActions> _authorizationService;

        public CreateBulkImportRequestHandler(
            IClaimsExtractor claimsExtractor,
            ITaskManager taskManager,
            ILogger<CreateBulkImportRequestHandler> logger,
            IAuthorizationService<DataActions> authorizationService)
        {
            EnsureArg.IsNotNull(claimsExtractor, nameof(claimsExtractor));
            EnsureArg.IsNotNull(taskManager, nameof(taskManager));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _claimsExtractor = claimsExtractor;
            _taskManager = taskManager;
            _authorizationService = authorizationService;
            _logger = logger;
        }

        public async Task<CreateBulkImportResponse> Handle(CreateBulkImportRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.Import, cancellationToken) != DataActions.Import)
            {
                throw new UnauthorizedFhirActionException();
            }

            var inputData = JsonSerializer.Serialize(request);
            var taskId = inputData.ToLowerInvariant().ComputeHash();
            var bulkImportTask = await _taskManager.GetTaskAsync(taskId, cancellationToken);
            if (bulkImportTask == null)
            {
                bulkImportTask = new TaskInfo()
                {
                    TaskId = taskId,
                    QueueId = "0",
                    TaskTypeId = BulkImportDataProcessingTask.BulkImportDataProcessingTaskTypeId,
                    InputData = inputData,
                };

                try
                {
                    _logger.LogInformation("Attempting to create bulk import task {0}", bulkImportTask.TaskId);
                    bulkImportTask = await _taskManager.CreateTaskAsync(bulkImportTask, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unable to create bulk import job {0} for request {1}", bulkImportTask.TaskId, request.RequestUri);
                    throw;
                }
            }

            return new CreateBulkImportResponse(bulkImportTask.TaskId);
        }
    }
}
