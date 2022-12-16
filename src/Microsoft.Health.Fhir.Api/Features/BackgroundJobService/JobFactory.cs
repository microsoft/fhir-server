// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Reflection;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.JobManagement;

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
        private readonly ISearchService _searchService;
        private readonly ILoggerFactory _loggerFactory;
        private readonly Dictionary<int, Func<IJob>> _jobFactoryLookup;

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
            ISearchService searchService,
            IEnumerable<Func<IJob>> jobFactories,
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
            EnsureArg.IsNotNull(searchService, nameof(searchService));
            EnsureArg.IsNotNull(jobFactories, nameof(jobFactories));
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
            _searchService = searchService;
            _loggerFactory = loggerFactory;

            _jobFactoryLookup = new Dictionary<int, Func<IJob>>();

            foreach (Func<IJob> jobFunc in jobFactories)
            {
                var instance = jobFunc.Invoke();
                if (instance.GetType().GetCustomAttribute(typeof(JobTypeIdAttribute), false) is JobTypeIdAttribute jobTypeAttr)
                {
                    _jobFactoryLookup.Add(jobTypeAttr.JobTypeId, jobFunc);
                }
                else
                {
                    throw new InvalidOperationException($"Job type {instance.GetType().Name} does not have {nameof(JobTypeIdAttribute)}.");
                }
            }
        }

        public IJob Create(JobInfo jobInfo)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));

            if (_jobFactoryLookup.TryGetValue(jobInfo.GetJobTypeId() ?? int.MinValue, out Func<IJob> jobFactory))
            {
                return jobFactory.Invoke();
            }

            throw new NotSupportedException($"Unknown task definition. ID: {jobInfo?.Id ?? -1}");
        }
    }
}
