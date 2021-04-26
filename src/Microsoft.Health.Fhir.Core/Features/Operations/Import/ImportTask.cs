// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.TaskManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportTask : ITask
    {
        public const short ResourceImportTaskId = 1;

        private ImportTaskInputData _inputData;
        private ImportProgress _importProgress;
        private IFhirDataBulkOperation _fhirDataBulkOperation;
        private IImportResourceLoader _importResourceLoader;
        private IResourceBulkImporter _resourceBulkImporter;
        private IImportErrorStoreFactory _importErrorStoreFactory;
        private IContextUpdater _contextUpdater;
        private IFhirRequestContextAccessor _contextAccessor;
        private ILogger<ImportTask> _logger;
        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        public ImportTask(
            ImportTaskInputData inputData,
            ImportProgress importProgress,
            IFhirDataBulkOperation fhirDataBulkOperation,
            IImportResourceLoader importResourceLoader,
            IResourceBulkImporter resourceBulkImporter,
            IImportErrorStoreFactory importErrorStoreFactory,
            IContextUpdater contextUpdater,
            IFhirRequestContextAccessor contextAccessor,
            ILoggerFactory loggerFactory)
        {
            EnsureArg.IsNotNull(inputData, nameof(inputData));
            EnsureArg.IsNotNull(importProgress, nameof(importProgress));
            EnsureArg.IsNotNull(fhirDataBulkOperation, nameof(fhirDataBulkOperation));
            EnsureArg.IsNotNull(importResourceLoader, nameof(importResourceLoader));
            EnsureArg.IsNotNull(resourceBulkImporter, nameof(resourceBulkImporter));
            EnsureArg.IsNotNull(importErrorStoreFactory, nameof(importErrorStoreFactory));
            EnsureArg.IsNotNull(contextUpdater, nameof(contextUpdater));
            EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));
            EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory));

            _inputData = inputData;
            _importProgress = importProgress;
            _fhirDataBulkOperation = fhirDataBulkOperation;
            _importResourceLoader = importResourceLoader;
            _resourceBulkImporter = resourceBulkImporter;
            _importErrorStoreFactory = importErrorStoreFactory;
            _contextUpdater = contextUpdater;
            _contextAccessor = contextAccessor;

            _logger = loggerFactory.CreateLogger<ImportTask>();
        }

        public string RunId { get; set; }

        public async Task<TaskResultData> ExecuteAsync()
        {
            var fhirRequestContext = new FhirRequestContext(
                    method: "Import",
                    uriString: _inputData.UriString,
                    baseUriString: _inputData.BaseUriString,
                    correlationId: _inputData.TaskId,
                    requestHeaders: new Dictionary<string, StringValues>(),
                    responseHeaders: new Dictionary<string, StringValues>())
            {
                IsBackgroundTask = true,
            };

            _contextAccessor.FhirRequestContext = fhirRequestContext;

            CancellationToken cancellationToken = _cancellationTokenSource.Token;

            long succeedImportCount = _importProgress.SucceedImportCount;
            long failedImportCount = _importProgress.FailedImportCount;

            ImportTaskResult result = new ImportTaskResult();
            result.ResourceType = _inputData.ResourceType;

            try
            {
                Func<long, long> idGenerator = (index) => _inputData.StartId + index;

                // Clean imported resource after last checkpoint.
                await CleanDataAsync(cancellationToken);

                // Initialize error store
                IImportErrorStore importErrorStore = await _importErrorStoreFactory.InitializeAsync(GetErrorFileName(), cancellationToken);
                result.ErrorLogLocation = importErrorStore.ErrorFileLocation;

                // Load and parse resource from bulk resource
                (Channel<ImportResource> importResourceChannel, Task loadTask) = _importResourceLoader.LoadResources(_inputData.ResourceLocation, _importProgress.EndIndex, idGenerator, cancellationToken);

                // Import to data store
                (Channel<ImportProgress> progressChannel, Task importTask) = _resourceBulkImporter.Import(importResourceChannel, importErrorStore, cancellationToken);

                // Update progress for checkpoints
                await foreach (ImportProgress progress in progressChannel.Reader.ReadAllAsync())
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException("Import task is canceled by user.");
                    }

                    _importProgress.SucceedImportCount = progress.SucceedImportCount + succeedImportCount;
                    _importProgress.FailedImportCount = progress.FailedImportCount + failedImportCount;
                    _importProgress.EndIndex = progress.EndIndex;
                    result.SucceedCount = _importProgress.SucceedImportCount;
                    result.FailedCount = _importProgress.FailedImportCount;

                    try
                    {
                        await _contextUpdater.UpdateContextAsync(JsonConvert.SerializeObject(_importProgress), cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        // ignore exception for progresss update
                        _logger.LogInformation(ex, "Failed to update context.");
                    }
                }

                // Pop up exception during load & import
                // Put import task before load task for resource channel full and blocking issue.
                try
                {
                    await importTask;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to import data.");
                    throw new RetriableTaskException("Failed to import data.", ex);
                }

                try
                {
                    await loadTask;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load data.");
                    throw new RetriableTaskException("Failed to load data", ex);
                }

                return new TaskResultData(TaskResult.Success, JsonConvert.SerializeObject(result));
            }
            catch (OperationCanceledException canceledEx)
            {
                _logger.LogError(canceledEx, "Data processing task is canceled.");
                return new TaskResultData(TaskResult.Canceled, JsonConvert.SerializeObject(result));
            }
            catch (RetriableTaskException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Critical error in data processing task.");

                result.ImportError = ex.Message;
                return new TaskResultData(TaskResult.Fail, JsonConvert.SerializeObject(result));
            }
            finally
            {
                _cancellationTokenSource.Cancel();
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
            return $"{_inputData.ResourceType}{_inputData.TaskId}.ndjson";
        }

        private async Task CleanDataAsync(CancellationToken cancellationToken)
        {
            long startId = _inputData.StartId;
            long endId = _inputData.EndId;
            long endIndex = _importProgress.EndIndex;

            try
            {
                await _fhirDataBulkOperation.CleanBatchResourceAsync(startId + endIndex, endId, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clean batch resource.");
                throw new RetriableTaskException("Failed to clean resource before import task start.", ex);
            }
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
