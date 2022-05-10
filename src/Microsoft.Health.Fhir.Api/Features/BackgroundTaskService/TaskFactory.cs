﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.TaskManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Api.Features.BackgroundTaskService
{
    /// <summary>
    /// Factory to create different tasks.
    /// </summary>
    public class TaskFactory : ITaskFactory
    {
        private readonly IImportResourceLoader _importResourceLoader;
        private readonly IResourceBulkImporter _resourceBulkImporter;
        private readonly IImportErrorStoreFactory _importErrorStoreFactory;
        private readonly IImportOrchestratorTaskDataStoreOperation _importOrchestratorTaskDataStoreOperation;
        private readonly ISequenceIdGenerator<long> _sequenceIdGenerator;
        private readonly IIntegrationDataStoreClient _integrationDataStoreClient;
        private readonly IQueueClient _queueClient;
        private readonly IContextUpdaterFactory _contextUpdaterFactory;
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;
        private readonly IMediator _mediator;
        private readonly OperationsConfiguration _operationsConfiguration;
        private readonly ILoggerFactory _loggerFactory;

        public TaskFactory(
            IImportResourceLoader importResourceLoader,
            IResourceBulkImporter resourceBulkImporter,
            IImportErrorStoreFactory importErrorStoreFactory,
            IImportOrchestratorTaskDataStoreOperation importOrchestratorTaskDataStoreOperation,
            IContextUpdaterFactory contextUpdaterFactory,
            IQueueClient queueClient,
            ISequenceIdGenerator<long> sequenceIdGenerator,
            IIntegrationDataStoreClient integrationDataStoreClient,
            RequestContextAccessor<IFhirRequestContext> contextAccessor,
            IOptions<OperationsConfiguration> operationsConfig,
            IMediator mediator,
            ILoggerFactory loggerFactory)
        {
            EnsureArg.IsNotNull(importResourceLoader, nameof(importResourceLoader));
            EnsureArg.IsNotNull(resourceBulkImporter, nameof(resourceBulkImporter));
            EnsureArg.IsNotNull(importErrorStoreFactory, nameof(importErrorStoreFactory));
            EnsureArg.IsNotNull(importOrchestratorTaskDataStoreOperation, nameof(importOrchestratorTaskDataStoreOperation));
            EnsureArg.IsNotNull(contextUpdaterFactory, nameof(contextUpdaterFactory));
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            EnsureArg.IsNotNull(sequenceIdGenerator, nameof(sequenceIdGenerator));
            EnsureArg.IsNotNull(integrationDataStoreClient, nameof(integrationDataStoreClient));
            EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory));

            _importResourceLoader = importResourceLoader;
            _resourceBulkImporter = resourceBulkImporter;
            _importErrorStoreFactory = importErrorStoreFactory;
            _importOrchestratorTaskDataStoreOperation = importOrchestratorTaskDataStoreOperation;
            _sequenceIdGenerator = sequenceIdGenerator;
            _integrationDataStoreClient = integrationDataStoreClient;
            _queueClient = queueClient;
            _contextUpdaterFactory = contextUpdaterFactory;
            _contextAccessor = contextAccessor;
            _mediator = mediator;
            _operationsConfiguration = operationsConfig.Value;
            _loggerFactory = loggerFactory;
        }

        public ITask Create(TaskInfo taskInfo)
        {
            EnsureArg.IsNotNull(taskInfo, nameof(taskInfo));

            ITask task = CreateProcessingTask(taskInfo);
            if (task != null)
            {
                return task;
            }

            task = CreateOrchestratorTask(taskInfo);

            return task;
        }

        private ITask CreateOrchestratorTask(TaskInfo taskInfo)
        {
            ImportOrchestratorTaskInputData inputData = JsonConvert.DeserializeObject<ImportOrchestratorTaskInputData>(taskInfo.Definition);
            if (inputData.TypeId == ImportOrchestratorTask.ImportOrchestratorTaskId)
            {
                ImportOrchestratorTaskResult currentResult = string.IsNullOrEmpty(taskInfo.Result) ? new ImportOrchestratorTaskResult() : JsonConvert.DeserializeObject<ImportOrchestratorTaskResult>(taskInfo.Result);

                return new ImportOrchestratorTask(
                    _mediator,
                    inputData,
                    currentResult,
                    _contextAccessor,
                    _importOrchestratorTaskDataStoreOperation,
                    _integrationDataStoreClient,
                    _queueClient,
                    taskInfo,
                    _operationsConfiguration.Import,
                    _loggerFactory);
            }
            else
            {
                return null;
            }
        }

        private ITask CreateProcessingTask(TaskInfo taskInfo)
        {
            ImportProcessingTaskInputData inputData = JsonConvert.DeserializeObject<ImportProcessingTaskInputData>(taskInfo.Definition);
            if (inputData.TypeId == ImportProcessingTask.ImportProcessingTaskId)
            {
                ImportProcessingTaskResult currentResult = string.IsNullOrEmpty(taskInfo.Result) ? new ImportProcessingTaskResult() : JsonConvert.DeserializeObject<ImportProcessingTaskResult>(taskInfo.Result);
                return new ImportProcessingTask(
                    inputData,
                    currentResult,
                    _importResourceLoader,
                    _resourceBulkImporter,
                    _importErrorStoreFactory,
                    _contextAccessor,
                    _loggerFactory);
            }
            else
            {
                return null;
            }
        }
    }
}
