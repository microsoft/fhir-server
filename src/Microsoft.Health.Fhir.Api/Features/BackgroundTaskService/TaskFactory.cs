// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.TaskManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Api.Features.BackgroundTaskService
{
    public class TaskFactory : ITaskFactory
    {
        private IFhirDataBulkOperation _fhirDataBulkOperation;
        private IContextUpdaterFactory _contextUpdaterFactory;
        private IBulkResourceLoader _resourceLoader;
        private IBulkRawResourceProcessor _rawResourceProcessor;
        private IImportErrorUploader _importErrorUploader;
        private IBulkImporter<BulkImportResourceWrapper> _bulkImporter;
        private IFhirRequestContextAccessor _contextAccessor;
        private ILoggerFactory _loggerFactory;

        public TaskFactory(
            IFhirDataBulkOperation fhirDataBulkOperation,
            IContextUpdaterFactory contextUpdaterFactory,
            IBulkResourceLoader resourceLoader,
            IBulkRawResourceProcessor rawResourceProcessor,
            IImportErrorUploader importErrorUploader,
            IBulkImporter<BulkImportResourceWrapper> bulkImporter,
            IFhirRequestContextAccessor contextAccessor,
            ILoggerFactory loggerFactory)
        {
            _fhirDataBulkOperation = fhirDataBulkOperation;
            _contextUpdaterFactory = contextUpdaterFactory;
            _resourceLoader = resourceLoader;
            _rawResourceProcessor = rawResourceProcessor;
            _importErrorUploader = importErrorUploader;
            _bulkImporter = bulkImporter;
            _contextAccessor = contextAccessor;
            _loggerFactory = loggerFactory;
        }

        public ITask Create(TaskInfo taskInfo)
        {
            if (taskInfo.TaskTypeId == BulkImportDataProcessingTask.BulkImportDataProcessingTaskTypeId)
            {
                IContextUpdater contextUpdater = _contextUpdaterFactory.CreateContextUpdater(taskInfo.TaskId, taskInfo.RunId);
                BulkImportDataProcessingInputData inputData = JsonConvert.DeserializeObject<BulkImportDataProcessingInputData>(taskInfo.InputData);
                BulkImportProgress bulkImportProgress = string.IsNullOrEmpty(taskInfo.Context) ? new BulkImportProgress() : JsonConvert.DeserializeObject<BulkImportProgress>(taskInfo.Context);
                return new BulkImportDataProcessingTask(
                    inputData,
                    bulkImportProgress,
                    _fhirDataBulkOperation,
                    contextUpdater,
                    _resourceLoader,
                    _importErrorUploader,
                    _rawResourceProcessor,
                    _bulkImporter,
                    _contextAccessor,
                    _loggerFactory);
            }

            return null;
        }
    }
}
