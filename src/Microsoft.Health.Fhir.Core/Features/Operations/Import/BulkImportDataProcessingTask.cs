// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.TaskManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class BulkImportDataProcessingTask : ITask
    {
        private const int RawDataChannelMaxCapacity = 3000;
        private const int ResourceWrapperChannelMaxCapacity = 3000;

        private BulkImportDataProcessingInputData _dataProcessingInputData;
        private BulkImportProgress _bulkImportProgress;
        private IFhirDataBulkOperation _fhirDataBulkOperation;
        private IContextUpdater _contextUpdater;
        private BulkResourceLoader _resourceLoader;
        private BulkRawResourceProcessor _rawResourceProcessor;
        private IBulkImporter<BulkImportResourceWrapper> _bulkImporter;
        private ILogger<BulkImportDataProcessingTask> _logger;
        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        public BulkImportDataProcessingTask(
            BulkImportDataProcessingInputData dataProcessingInputData,
            BulkImportProgress bulkImportProgress,
            IFhirDataBulkOperation fhirDataBulkOperation,
            IContextUpdater contextUpdater,
            BulkResourceLoader resourceLoader,
            BulkRawResourceProcessor rawResourceProcessor,
            IBulkImporter<BulkImportResourceWrapper> bulkImporter,
            ILoggerFactory loggerFactory)
        {
            _dataProcessingInputData = dataProcessingInputData;
            _bulkImportProgress = bulkImportProgress;
            _fhirDataBulkOperation = fhirDataBulkOperation;
            _contextUpdater = contextUpdater;
            _resourceLoader = resourceLoader;
            _rawResourceProcessor = rawResourceProcessor;
            _bulkImporter = bulkImporter;

            _logger = loggerFactory.CreateLogger<BulkImportDataProcessingTask>();
        }

        public string RunId { get; set; }

        public async Task<TaskResultData> ExecuteAsync()
        {
            CancellationToken cancellationToken = _cancellationTokenSource.Token;

            long lastCompletedSurrogateId = await CleanDataAsync(cancellationToken);
            long startLineOffset = lastCompletedSurrogateId - _dataProcessingInputData.StartSurrogateId;

            Channel<string> rawDataChannel = Channel.CreateBounded<string>(RawDataChannelMaxCapacity);
            Channel<BulkImportResourceWrapper> resourceWrapperChannel = Channel.CreateBounded<BulkImportResourceWrapper>(ResourceWrapperChannelMaxCapacity);
            IProgress<(string tableName, long endSurrogateId)> bulkImportProgress = new Progress<(string tableName, long endSurrogateId)>(
                (p) =>
                {
                    _bulkImportProgress.ProgressRecords[p.tableName] = new ProgressRecord(p.endSurrogateId);
                });

            Task dataLoadTask = _resourceLoader.LoadToChannelAsync(rawDataChannel, new Uri(_dataProcessingInputData.ResourceLocation), startLineOffset, cancellationToken);
            Task processTask = _rawResourceProcessor.ProcessingDataAsync(rawDataChannel, resourceWrapperChannel, lastCompletedSurrogateId, cancellationToken);
            Task<long> bulkImportTask = _bulkImporter.ImportResourceAsync(resourceWrapperChannel, bulkImportProgress, cancellationToken);

            CancellationTokenSource contextUpdateCancellationToken = new CancellationTokenSource();
            Task updateProgressTask = UpdateProgressAsync(contextUpdateCancellationToken.Token);

            await dataLoadTask;
            await processTask;
            long completedResourceCount = await bulkImportTask;

            contextUpdateCancellationToken.Cancel();
            await updateProgressTask;

            BulkImportDataProcessingTaskResult result = new BulkImportDataProcessingTaskResult()
            {
                ResourceType = _dataProcessingInputData.ResourceType,
                CompletedResourceCount = completedResourceCount,
            };

            return new TaskResultData(TaskResult.Success, JsonConvert.SerializeObject(result));
        }

        public void Cancel()
        {
            _cancellationTokenSource?.Cancel();
        }

        public bool IsCancelling()
        {
            return _cancellationTokenSource?.IsCancellationRequested ?? true;
        }

        private async Task<long> CleanDataAsync(CancellationToken cancellationToken)
        {
            long lastCompletedSurrogateId = _dataProcessingInputData.StartSurrogateId;
            long lastOffset = 0;
            long endSurrogateId = _dataProcessingInputData.EndSurrogateId;
            if (_bulkImportProgress.ProgressRecords.Count > 0)
            {
                lastOffset = _bulkImportProgress.ProgressRecords.Values.Min(r => r.LastOffset);
                lastCompletedSurrogateId = _bulkImportProgress.ProgressRecords.Values.Min(r => r.LastSurrogatedId);
            }

            await _fhirDataBulkOperation.CleanBatchResourceAsync(lastCompletedSurrogateId, endSurrogateId, cancellationToken);

            ProgressRecord record = new ProgressRecord()
            {
                LastOffset = lastOffset,
                LastSurrogatedId = lastCompletedSurrogateId,
            };
            foreach (var resourceType in _bulkImportProgress.ProgressRecords.Keys)
            {
                _bulkImportProgress.ProgressRecords[resourceType] = record;
            }

            await _contextUpdater.UpdateContextAsync(JsonConvert.SerializeObject(_bulkImportProgress), cancellationToken);

            return lastCompletedSurrogateId;
        }

        private async Task UpdateProgressAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await _contextUpdater.UpdateContextAsync(JsonConvert.SerializeObject(_bulkImportProgress), cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(10));
            }

            await _contextUpdater.UpdateContextAsync(JsonConvert.SerializeObject(_bulkImportProgress), cancellationToken);
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
