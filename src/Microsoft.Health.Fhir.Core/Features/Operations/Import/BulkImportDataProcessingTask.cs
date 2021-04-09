// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.TaskManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class BulkImportDataProcessingTask : ITask
    {
        private BulkImportDataProcessingInputData _dataProcessingInputData;
        private BulkImportProgress _bulkImportProgress;
        private IIntegrationDataStoreClient _integrationDataStoreClient;
        private IFhirDataBulkOperation _fhirDataBulkOperation;
        private IContextUpdater _contextUpdater;
        private IBulkImportDataExtractor _bulkImportDataExtractor;
        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        public BulkImportDataProcessingTask(
            BulkImportDataProcessingInputData dataProcessingInputData,
            BulkImportProgress bulkImportProgress,
            IIntegrationDataStoreClient integrationDataStoreClient,
            IFhirDataBulkOperation fhirDataBulkOperation,
            IContextUpdater contextUpdater,
            IBulkImportDataExtractor bulkImportDataExtractor)
        {
            _dataProcessingInputData = dataProcessingInputData;
            _bulkImportProgress = bulkImportProgress;
            _integrationDataStoreClient = integrationDataStoreClient;
            _fhirDataBulkOperation = fhirDataBulkOperation;
            _contextUpdater = contextUpdater;
            _bulkImportDataExtractor = bulkImportDataExtractor;
        }

        public string RunId { get; set; }

        public async Task<TaskResultData> ExecuteAsync()
        {
            CancellationToken cancellationToken = _cancellationTokenSource.Token;

            long lastOffset = await CleanDataAsync(cancellationToken);

            return null;
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

            await _fhirDataBulkOperation.CleanResourceAsync(lastCompletedSurrogateId, endSurrogateId, cancellationToken);

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

            return lastOffset;
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
