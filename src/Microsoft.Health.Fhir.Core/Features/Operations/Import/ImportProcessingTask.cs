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
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.TaskManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportProcessingTask : ITask
    {
        public const short ImportProcessingTaskId = 1;

        private ImportProcessingTaskInputData _inputData;
        private ImportProcessingProgress _importProgress;
        private IImportResourceLoader _importResourceLoader;
        private IResourceBulkImporter _resourceBulkImporter;
        private IImportErrorStoreFactory _importErrorStoreFactory;
        private IContextUpdater _contextUpdater;
        private RequestContextAccessor<IFhirRequestContext> _contextAccessor;
        private ILogger<ImportProcessingTask> _logger;
        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        public ImportProcessingTask(
            ImportProcessingTaskInputData inputData,
            ImportProcessingProgress importProgress,
            IImportResourceLoader importResourceLoader,
            IResourceBulkImporter resourceBulkImporter,
            IImportErrorStoreFactory importErrorStoreFactory,
            IContextUpdater contextUpdater,
            RequestContextAccessor<IFhirRequestContext> contextAccessor,
            ILoggerFactory loggerFactory)
        {
            EnsureArg.IsNotNull(inputData, nameof(inputData));
            EnsureArg.IsNotNull(importProgress, nameof(importProgress));
            EnsureArg.IsNotNull(importResourceLoader, nameof(importResourceLoader));
            EnsureArg.IsNotNull(resourceBulkImporter, nameof(resourceBulkImporter));
            EnsureArg.IsNotNull(importErrorStoreFactory, nameof(importErrorStoreFactory));
            EnsureArg.IsNotNull(contextUpdater, nameof(contextUpdater));
            EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));
            EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory));

            _inputData = inputData;
            _importProgress = importProgress;
            _importResourceLoader = importResourceLoader;
            _resourceBulkImporter = resourceBulkImporter;
            _importErrorStoreFactory = importErrorStoreFactory;
            _contextUpdater = contextUpdater;
            _contextAccessor = contextAccessor;

            _logger = loggerFactory.CreateLogger<ImportProcessingTask>();
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

            _contextAccessor.RequestContext = fhirRequestContext;

            CancellationToken cancellationToken = _cancellationTokenSource.Token;

            long succeedImportCount = _importProgress.SucceedImportCount;
            long failedImportCount = _importProgress.FailedImportCount;

            ImportProcessingTaskResult result = new ImportProcessingTaskResult();
            result.ResourceType = _inputData.ResourceType;

            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException();
                }

                Func<long, long> sequenceIdGenerator = (index) => _inputData.BeginSequenceId + index;

                // Clean resources before import start
                await _resourceBulkImporter.CleanResourceAsync(_inputData, _importProgress, cancellationToken);
                _importProgress.NeedCleanData = true;
                await _contextUpdater.UpdateContextAsync(JsonConvert.SerializeObject(_importProgress), cancellationToken);

                // Initialize error store
                IImportErrorStore importErrorStore = await _importErrorStoreFactory.InitializeAsync(GetErrorFileName(), cancellationToken);
                result.ErrorLogLocation = importErrorStore.ErrorFileLocation;

                // Load and parse resource from bulk resource
                (Channel<ImportResource> importResourceChannel, Task loadTask) = _importResourceLoader.LoadResources(_inputData.ResourceLocation, _importProgress.CurrentIndex, _inputData.ResourceType, sequenceIdGenerator, cancellationToken);

                // Import to data store
                (Channel<ImportProcessingProgress> progressChannel, Task importTask) = _resourceBulkImporter.Import(importResourceChannel, importErrorStore, cancellationToken);

                // Update progress for checkpoints
                await foreach (ImportProcessingProgress progress in progressChannel.Reader.ReadAllAsync())
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException("Import task is canceled by user.");
                    }

                    _importProgress.SucceedImportCount = progress.SucceedImportCount + succeedImportCount;
                    _importProgress.FailedImportCount = progress.FailedImportCount + failedImportCount;
                    _importProgress.CurrentIndex = progress.CurrentIndex;
                    result.SucceedCount = _importProgress.SucceedImportCount;
                    result.FailedCount = _importProgress.FailedImportCount;

                    _logger.LogInformation("Import task progress: {0}", JsonConvert.SerializeObject(_importProgress));

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
                catch (TaskCanceledException)
                {
                    throw;
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
                catch (TaskCanceledException)
                {
                    throw;
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
            catch (TaskCanceledException canceledEx)
            {
                _logger.LogInformation(canceledEx, "Data processing task is canceled.");

                await CleanResourceForFailureAsync(canceledEx);

                return new TaskResultData(TaskResult.Canceled, JsonConvert.SerializeObject(result));
            }
            catch (OperationCanceledException canceledEx)
            {
                _logger.LogInformation(canceledEx, "Data processing task is canceled.");

                await CleanResourceForFailureAsync(canceledEx);

                return new TaskResultData(TaskResult.Canceled, JsonConvert.SerializeObject(result));
            }
            catch (RetriableTaskException retriableEx)
            {
                _logger.LogInformation(retriableEx, "Error in data processing task.");

                await CleanResourceForFailureAsync(retriableEx);

                throw;
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "Critical error in data processing task.");

                await CleanResourceForFailureAsync(ex);

                throw new RetriableTaskException(ex.Message);
            }
            finally
            {
                if (!_cancellationTokenSource.IsCancellationRequested)
                {
                    _cancellationTokenSource.Cancel();
                }
            }
        }

        private async Task CleanResourceForFailureAsync(Exception failureException)
        {
            try
            {
                await _resourceBulkImporter.CleanResourceAsync(_inputData, _importProgress, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "Data processing task is canceled. Failed to clean resource.");
                throw new RetriableTaskException(ex.Message, failureException);
            }
        }

        public void Cancel()
        {
            if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
            {
                _cancellationTokenSource?.Cancel();
            }
        }

        public bool IsCancelling()
        {
            return _cancellationTokenSource?.IsCancellationRequested ?? false;
        }

        private string GetErrorFileName()
        {
            return $"{_inputData.ResourceType}{_inputData.TaskId}.ndjson";
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
