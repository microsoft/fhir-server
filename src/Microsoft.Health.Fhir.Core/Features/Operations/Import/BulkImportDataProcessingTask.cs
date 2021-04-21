// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.TaskManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class BulkImportDataProcessingTask : ITask
    {
        public const short BulkImportDataProcessingTaskTypeId = 1;

        private const int RawDataChannelMaxCapacity = 3000;
        private const int ResourceWrapperChannelMaxCapacity = 3000;
        private const string ErrorResourceTableName = "ErrorResourceTable";

        private BulkImportDataProcessingInputData _dataProcessingInputData;
        private BulkImportProgress _bulkImportProgress;
        private IFhirDataBulkOperation _fhirDataBulkOperation;
        private IContextUpdater _contextUpdater;
        private IBulkResourceLoader _resourceLoader;
        private IImportErrorUploader _importErrorUploader;
        private IBulkRawResourceProcessor _rawResourceProcessor;
        private IBulkImporter<BulkImportResourceWrapper> _bulkImporter;
        private IFhirRequestContextAccessor _contextAccessor;
        private ILogger<BulkImportDataProcessingTask> _logger;
        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        public BulkImportDataProcessingTask(
            BulkImportDataProcessingInputData dataProcessingInputData,
            BulkImportProgress bulkImportProgress,
            IFhirDataBulkOperation fhirDataBulkOperation,
            IContextUpdater contextUpdater,
            IBulkResourceLoader resourceLoader,
            IImportErrorUploader importErrorUploader,
            IBulkRawResourceProcessor rawResourceProcessor,
            IBulkImporter<BulkImportResourceWrapper> bulkImporter,
            IFhirRequestContextAccessor contextAccessor,
            ILoggerFactory loggerFactory)
        {
            _dataProcessingInputData = dataProcessingInputData;
            _bulkImportProgress = bulkImportProgress;
            _fhirDataBulkOperation = fhirDataBulkOperation;
            _contextUpdater = contextUpdater;
            _resourceLoader = resourceLoader;
            _importErrorUploader = importErrorUploader;
            _rawResourceProcessor = rawResourceProcessor;
            _bulkImporter = bulkImporter;
            _contextAccessor = contextAccessor;

            _logger = loggerFactory.CreateLogger<BulkImportDataProcessingTask>();
        }

        public string RunId { get; set; }

        public async Task<TaskResultData> ExecuteAsync()
        {
            var fhirRequestContext = new FhirRequestContext(
                    method: "Import",
                    uriString: _dataProcessingInputData.UriString,
                    baseUriString: _dataProcessingInputData.BaseUriString,
                    correlationId: _dataProcessingInputData.TaskId,
                    requestHeaders: new Dictionary<string, StringValues>(),
                    responseHeaders: new Dictionary<string, StringValues>())
            {
                IsBackgroundTask = true,
            };

            _contextAccessor.FhirRequestContext = fhirRequestContext;

            CancellationToken cancellationToken = _cancellationTokenSource.Token;

            try
            {
                long lastCompletedSurrogateId = await CleanDataAsync(cancellationToken);
                long startLineOffset = lastCompletedSurrogateId - _dataProcessingInputData.StartSurrogateId;

                Channel<string> rawDataChannel = Channel.CreateBounded<string>(RawDataChannelMaxCapacity);
                Channel<BulkImportResourceWrapper> resourceWrapperChannel = Channel.CreateBounded<BulkImportResourceWrapper>(ResourceWrapperChannelMaxCapacity);
                Channel<BatchProcessErrorRecord> processErrorChannel = Channel.CreateBounded<BatchProcessErrorRecord>(ResourceWrapperChannelMaxCapacity);
                Action<(string tableName, long endSurrogateId)> progressUpdateAction =
                    progress =>
                    {
                        _bulkImportProgress.ProgressRecords[progress.tableName] = new ProgressRecord(progress.endSurrogateId);
                    };
                Action<long, long> errorProgressUpdateAction =
                    (lastBatchId, endSurrogateId) =>
                    {
                        _bulkImportProgress.ProgressRecords[ErrorResourceTableName] = new ProgressRecord(endSurrogateId);
                        _bulkImportProgress.CurrentErrorLogBatchId = lastBatchId;
                    };

                Task dataLoadTask = _resourceLoader.LoadToChannelAsync(rawDataChannel, new Uri(_dataProcessingInputData.ResourceLocation), startLineOffset, cancellationToken);
                Task<long> processTask = _rawResourceProcessor.ProcessingDataAsync(rawDataChannel, resourceWrapperChannel, processErrorChannel, lastCompletedSurrogateId, cancellationToken);
                Task<long> bulkImportTask = _bulkImporter.ImportResourceAsync(resourceWrapperChannel, progressUpdateAction, cancellationToken);
                Task<(long, Uri)> errorHandleTask = _importErrorUploader.HandleImportErrorAsync(GetErrorFileName(), processErrorChannel, _bulkImportProgress.CurrentErrorLogBatchId, errorProgressUpdateAction, cancellationToken);

                CancellationTokenSource contextUpdateCancellationToken = new CancellationTokenSource();
                Task updateProgressTask = UpdateProgressAsync(contextUpdateCancellationToken.Token);

                await dataLoadTask;
                long completedResourceCount = await processTask;
                long importedSqlRecordCount = await bulkImportTask;
                (long errorCount, Uri errorFileUri) = await errorHandleTask;

                _logger.LogInformation($"{completedResourceCount} resources imported. {importedSqlRecordCount} sql records copied to sql store.");

                contextUpdateCancellationToken.Cancel();
                await updateProgressTask;

                BulkImportDataProcessingTaskResult result = new BulkImportDataProcessingTaskResult()
                {
                    ResourceType = _dataProcessingInputData.ResourceType,
                    CompletedResourceCount = completedResourceCount,
                    FailedResourceCount = errorCount,
                    ErrorLogLocation = errorFileUri.ToString(),
                };

                if (!cancellationToken.IsCancellationRequested)
                {
                    return new TaskResultData(TaskResult.Success, JsonConvert.SerializeObject(result));
                }
                else
                {
                    return new TaskResultData(TaskResult.Canceled, JsonConvert.SerializeObject(result));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Critical error in data processing task.");

                throw;
            }
        }

        public void Cancel()
        {
            _cancellationTokenSource?.Cancel();
        }

        public bool IsCancelling()
        {
            return _cancellationTokenSource?.IsCancellationRequested ?? false;
        }

        private string GetErrorFileName()
        {
            return $"{_dataProcessingInputData.ResourceType}-{_dataProcessingInputData.TaskId}.ndjson";
        }

        private async Task<long> CleanDataAsync(CancellationToken cancellationToken)
        {
            // For first run, last completed resource surrogated id == -1
            long lastCompletedSurrogateId = _dataProcessingInputData.StartSurrogateId - 1;
            long endSurrogateId = _dataProcessingInputData.EndSurrogateId;
            if (_bulkImportProgress.ProgressRecords.Count > 0)
            {
                lastCompletedSurrogateId = _bulkImportProgress.ProgressRecords.Values.Min(r => r.LastSurrogatedId);
            }

            try
            {
                await _fhirDataBulkOperation.CleanBatchResourceAsync(lastCompletedSurrogateId + 1, endSurrogateId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clean batch resource.");
                throw;
            }

            ProgressRecord record = new ProgressRecord()
            {
                LastSurrogatedId = lastCompletedSurrogateId,
            };
            foreach (var resourceType in _bulkImportProgress.ProgressRecords.Keys)
            {
                _bulkImportProgress.ProgressRecords[resourceType] = record;
            }

            await _contextUpdater.UpdateContextAsync(JsonConvert.SerializeObject(_bulkImportProgress), CancellationToken.None);

            return lastCompletedSurrogateId;
        }

        private async Task UpdateProgressAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await _contextUpdater.UpdateContextAsync(JsonConvert.SerializeObject(_bulkImportProgress), CancellationToken.None);
                await Task.Delay(TimeSpan.FromSeconds(10));
            }

            await _contextUpdater.UpdateContextAsync(JsonConvert.SerializeObject(_bulkImportProgress), CancellationToken.None);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }
    }
}
