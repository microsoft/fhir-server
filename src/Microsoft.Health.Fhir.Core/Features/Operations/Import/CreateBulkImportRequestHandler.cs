﻿// -------------------------------------------------------------------------------------------------
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
using Microsoft.Extensions.Options;
using Microsoft.Health.Core;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Import;
using Microsoft.Health.TaskManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    /// <summary>
    /// MediatR request handler. Called when the BulkImportController creates an BulkImport job.
    /// </summary>
    public class CreateBulkImportRequestHandler : IRequestHandler<CreateImportRequest, CreateImportResponse>
    {
        private readonly ITaskManager _taskManager;
        private readonly ImportTaskConfiguration _importTaskConfiguration;
        private readonly TaskHostingConfiguration _taskHostingConfiguration;
        private readonly ILogger<CreateBulkImportRequestHandler> _logger;
        private readonly IAuthorizationService<DataActions> _authorizationService;

        public CreateBulkImportRequestHandler(
            ITaskManager taskManager,
            IOptions<OperationsConfiguration> operationsConfig,
            IOptions<TaskHostingConfiguration> taskHostingConfiguration,
            ILogger<CreateBulkImportRequestHandler> logger,
            IAuthorizationService<DataActions> authorizationService)
        {
            EnsureArg.IsNotNull(taskManager, nameof(taskManager));
            EnsureArg.IsNotNull(operationsConfig.Value, nameof(operationsConfig));
            EnsureArg.IsNotNull(taskHostingConfiguration.Value, nameof(taskHostingConfiguration));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _taskManager = taskManager;
            _importTaskConfiguration = operationsConfig.Value.Import;
            _taskHostingConfiguration = taskHostingConfiguration.Value;
            _authorizationService = authorizationService;
            _logger = logger;
        }

        public async Task<CreateImportResponse> Handle(CreateImportRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.Import, cancellationToken) != DataActions.Import)
            {
                throw new UnauthorizedFhirActionException();
            }

            var activeImportTasks = await _taskManager.GetActiveTasksByTypeAsync(ImportOrchestratorTask.ImportOrchestratorTaskId, cancellationToken);
            if (activeImportTasks.Count > 0)
            {
                throw new OperationFailedException(Resources.ImportTaskIsRunning, HttpStatusCode.Conflict);
            }

            string taskId = Guid.NewGuid().ToString("N");

            string processingTaskQueueId = string.IsNullOrEmpty(_importTaskConfiguration.ProcessingTaskQueueId) ? _taskHostingConfiguration.QueueId : _importTaskConfiguration.ProcessingTaskQueueId;
            ImportOrchestratorTaskInputData inputData = new ImportOrchestratorTaskInputData()
            {
                RequestUri = request.RequestUri,
                BaseUri = new Uri(request.RequestUri.GetLeftPart(UriPartial.Authority)),
                Input = request.Input,
                InputFormat = request.InputFormat,
                InputSource = request.InputSource,
                StorageDetail = request.StorageDetail,
                MaxConcurrentProcessingTaskCount = _importTaskConfiguration.MaximumConcurrency,
                MaxConcurrentRebuildIndexOperationCount = _importTaskConfiguration.MaximumConcurrentRebuildOperationCount,
                ProcessingTaskQueueId = processingTaskQueueId,
                TaskId = taskId,
                TaskCreateTime = Clock.UtcNow,
            };

            TaskInfo taskInfo = new TaskInfo()
            {
                TaskId = taskId,
                TaskTypeId = ImportOrchestratorTask.ImportOrchestratorTaskId,
                MaxRetryCount = 5,
                QueueId = _taskHostingConfiguration.QueueId,
                InputData = JsonConvert.SerializeObject(inputData),
            };

            await _taskManager.CreateTaskAsync(taskInfo, cancellationToken);

            return new CreateImportResponse(taskId);
        }
    }
}
