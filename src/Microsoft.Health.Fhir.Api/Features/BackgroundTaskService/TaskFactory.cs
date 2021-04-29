// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Health.Core.Features.Context;
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
        private IImportResourceLoader _importResourceLoader;
        private IResourceBulkImporter _resourceBulkImporter;
        private IImportErrorStoreFactory _importErrorStoreFactory;
        private IContextUpdaterFactory _contextUpdaterFactory;
        private RequestContextAccessor<IFhirRequestContext> _contextAccessor;
        private ILoggerFactory _loggerFactory;

        public TaskFactory(
            IFhirDataBulkOperation fhirDataBulkOperation,
            IImportResourceLoader importResourceLoader,
            IResourceBulkImporter resourceBulkImporter,
            IImportErrorStoreFactory importErrorStoreFactory,
            IContextUpdaterFactory contextUpdaterFactory,
            RequestContextAccessor<IFhirRequestContext> contextAccessor,
            ILoggerFactory loggerFactory)
        {
            _fhirDataBulkOperation = fhirDataBulkOperation;
            _importResourceLoader = importResourceLoader;
            _resourceBulkImporter = resourceBulkImporter;
            _importErrorStoreFactory = importErrorStoreFactory;
            _contextUpdaterFactory = contextUpdaterFactory;
            _contextAccessor = contextAccessor;
            _loggerFactory = loggerFactory;
        }

        public ITask Create(TaskInfo taskInfo)
        {
            if (taskInfo.TaskTypeId == ImportTask.ResourceImportTaskId)
            {
                IContextUpdater contextUpdater = _contextUpdaterFactory.CreateContextUpdater(taskInfo.TaskId, taskInfo.RunId);
                ImportTaskInputData inputData = JsonConvert.DeserializeObject<ImportTaskInputData>(taskInfo.InputData);
                ImportProgress importProgress = string.IsNullOrEmpty(taskInfo.Context) ? new ImportProgress() : JsonConvert.DeserializeObject<ImportProgress>(taskInfo.Context);
                return new ImportTask(
                    inputData,
                    importProgress,
                    _fhirDataBulkOperation,
                    _importResourceLoader,
                    _resourceBulkImporter,
                    _importErrorStoreFactory,
                    contextUpdater,
                    _contextAccessor,
                    _loggerFactory);
            }

            return null;
        }
    }
}
