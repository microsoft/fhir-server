// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Api.Features.BackgroundJobService
{
    /// <summary>
    /// Factory to create different tasks.
    /// </summary>
    public class JobFactory : IJobFactory
    {
        private readonly IImportResourceLoader _importResourceLoader;
        private readonly IResourceBulkImporter _resourceBulkImporter;
        private readonly IImportErrorStoreFactory _importErrorStoreFactory;
        private readonly IImportOrchestratorJobDataStoreOperation _importOrchestratorTaskDataStoreOperation;
        private readonly IIntegrationDataStoreClient _integrationDataStoreClient;
        private readonly IQueueClient _queueClient;
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;
        private readonly IMediator _mediator;
        private readonly OperationsConfiguration _operationsConfiguration;
        private readonly Func<IExportJobTask> _exportJobTaskFactory;
        private readonly ILoggerFactory _loggerFactory;

        public JobFactory(
            IImportResourceLoader importResourceLoader,
            IResourceBulkImporter resourceBulkImporter,
            IImportErrorStoreFactory importErrorStoreFactory,
            IImportOrchestratorJobDataStoreOperation importOrchestratorTaskDataStoreOperation,
            IQueueClient queueClient,
            IIntegrationDataStoreClient integrationDataStoreClient,
            RequestContextAccessor<IFhirRequestContext> contextAccessor,
            IOptions<OperationsConfiguration> operationsConfig,
            IMediator mediator,
            Func<IExportJobTask> exportJobTaskFactory,
            ILoggerFactory loggerFactory)
        {
            EnsureArg.IsNotNull(importResourceLoader, nameof(importResourceLoader));
            EnsureArg.IsNotNull(resourceBulkImporter, nameof(resourceBulkImporter));
            EnsureArg.IsNotNull(importErrorStoreFactory, nameof(importErrorStoreFactory));
            EnsureArg.IsNotNull(importOrchestratorTaskDataStoreOperation, nameof(importOrchestratorTaskDataStoreOperation));
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            EnsureArg.IsNotNull(integrationDataStoreClient, nameof(integrationDataStoreClient));
            EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(exportJobTaskFactory, nameof(exportJobTaskFactory));
            EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory));

            _importResourceLoader = importResourceLoader;
            _resourceBulkImporter = resourceBulkImporter;
            _importErrorStoreFactory = importErrorStoreFactory;
            _importOrchestratorTaskDataStoreOperation = importOrchestratorTaskDataStoreOperation;
            _integrationDataStoreClient = integrationDataStoreClient;
            _queueClient = queueClient;
            _contextAccessor = contextAccessor;
            _mediator = mediator;
            _operationsConfiguration = operationsConfig.Value;
            _exportJobTaskFactory = exportJobTaskFactory;
            _loggerFactory = loggerFactory;
        }

        public IJob Create(JobInfo jobInfo)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));

            Func<JobInfo, IJob>[] taskFactoryFuncs =
                new Func<JobInfo, IJob>[]
                {
                    CreateImportProcessingTask,
                    CreateImportOrchestratorTask,
                    CreateExportOrchestratorJob,
                    CreateExportProcesingJob,
                };

            foreach (Func<JobInfo, IJob> factoryFunc in taskFactoryFuncs)
            {
                IJob task = factoryFunc(jobInfo);
                if (task != null)
                {
                    return task;
                }
            }

            throw new NotSupportedException($"Unknown task definition. ID: {jobInfo?.Id ?? -1}");
        }

        private IJob CreateImportOrchestratorTask(JobInfo taskInfo)
        {
            ImportOrchestratorJobInputData inputData = JsonConvert.DeserializeObject<ImportOrchestratorJobInputData>(taskInfo.Definition);
            if (inputData.TypeId == (int)JobType.ImportOrchestrator)
            {
                ImportOrchestratorJobResult currentResult = string.IsNullOrEmpty(taskInfo.Result) ? new ImportOrchestratorJobResult() : JsonConvert.DeserializeObject<ImportOrchestratorJobResult>(taskInfo.Result);

                return new ImportOrchestratorJob(
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

        private IJob CreateImportProcessingTask(JobInfo taskInfo)
        {
            ImportProcessingJobInputData inputData = JsonConvert.DeserializeObject<ImportProcessingJobInputData>(taskInfo.Definition);
            if (inputData.TypeId == (int)JobType.ImportProcessing)
            {
                ImportProcessingJobResult currentResult = string.IsNullOrEmpty(taskInfo.Result) ? new ImportProcessingJobResult() : JsonConvert.DeserializeObject<ImportProcessingJobResult>(taskInfo.Result);
                return new ImportProcessingJob(
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

        private IJob CreateExportOrchestratorJob(JobInfo jobInfo)
        {
            ExportJobRecord exportData = JsonConvert.DeserializeObject<ExportJobRecord>(jobInfo.Definition);
            if (exportData.TypeId == (int)JobType.ExportOrchestrator)
            {
                return new ExportOrchestratorJob(jobInfo, _queueClient, _loggerFactory);
            }
            else
            {
                return null;
            }
        }

        private IJob CreateExportProcesingJob(JobInfo jobInfo)
        {
            ExportJobRecord exportData = JsonConvert.DeserializeObject<ExportJobRecord>(jobInfo.Definition);
            if (exportData.TypeId == (int)JobType.ExportProcessing)
            {
                return new ExportProcessingJob(jobInfo, _exportJobTaskFactory);
            }
            else
            {
                return null;
            }
        }
    }
}
