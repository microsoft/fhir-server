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
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportProcessingJob : IJob
    {
        public const short ImportProcessingJobTypeId = 1;
        public const string CancelledErrorMessage = "Data processing job is canceled.";

        private ImportProcessingJobInputData _inputData;
        private ImportProcessingJobResult _importResult;
        private IImportResourceLoader _importResourceLoader;
        private IResourceBulkImporter _resourceBulkImporter;
        private IImportErrorStoreFactory _importErrorStoreFactory;
        private RequestContextAccessor<IFhirRequestContext> _contextAccessor;
        private ILogger<ImportProcessingJob> _logger;

        public ImportProcessingJob(
            ImportProcessingJobInputData inputData,
            ImportProcessingJobResult importResult,
            IImportResourceLoader importResourceLoader,
            IResourceBulkImporter resourceBulkImporter,
            IImportErrorStoreFactory importErrorStoreFactory,
            RequestContextAccessor<IFhirRequestContext> contextAccessor,
            ILoggerFactory loggerFactory)
        {
            EnsureArg.IsNotNull(inputData, nameof(inputData));
            EnsureArg.IsNotNull(importResult, nameof(importResult));
            EnsureArg.IsNotNull(importResourceLoader, nameof(importResourceLoader));
            EnsureArg.IsNotNull(resourceBulkImporter, nameof(resourceBulkImporter));
            EnsureArg.IsNotNull(importErrorStoreFactory, nameof(importErrorStoreFactory));
            EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));
            EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory));

            _inputData = inputData;
            _importResult = importResult;
            _importResourceLoader = importResourceLoader;
            _resourceBulkImporter = resourceBulkImporter;
            _importErrorStoreFactory = importErrorStoreFactory;
            _contextAccessor = contextAccessor;

            _logger = loggerFactory.CreateLogger<ImportProcessingJob>();
        }

        public string RunId { get; set; }

        public async Task<string> ExecuteAsync(IProgress<string> progress, CancellationToken cancellationToken)
        {
            var fhirRequestContext = new FhirRequestContext(
                    method: "Import",
                    uriString: _inputData.UriString,
                    baseUriString: _inputData.BaseUriString,
                    correlationId: _inputData.JobId,
                    requestHeaders: new Dictionary<string, StringValues>(),
                    responseHeaders: new Dictionary<string, StringValues>())
            {
                IsBackgroundTask = true,
            };

            _contextAccessor.RequestContext = fhirRequestContext;

            long succeedImportCount = _importResult.SucceedCount;
            long failedImportCount = _importResult.FailedCount;

            _importResult.ResourceType = _inputData.ResourceType;
            _importResult.ResourceLocation = _inputData.ResourceLocation;
            progress.Report(JsonConvert.SerializeObject(_importResult));

            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException();
                }

                Func<long, long> sequenceIdGenerator = (index) => _inputData.BeginSequenceId + index;

                // Clean resources before import start
                await _resourceBulkImporter.CleanResourceAsync(_inputData, _importResult, cancellationToken);

                // Initialize error store
                IImportErrorStore importErrorStore = await _importErrorStoreFactory.InitializeAsync(GetErrorFileName(), cancellationToken);
                _importResult.ErrorLogLocation = importErrorStore.ErrorFileLocation;

                // Load and parse resource from bulk resource
                (Channel<ImportResource> importResourceChannel, Task loadTask) = _importResourceLoader.LoadResources(_inputData.ResourceLocation, _importResult.CurrentIndex, _inputData.ResourceType, sequenceIdGenerator, cancellationToken);

                // Import to data store
                (Channel<ImportProcessingProgress> progressChannel, Task importTask) = _resourceBulkImporter.Import(importResourceChannel, importErrorStore, cancellationToken);

                // Update progress for checkpoints
                await foreach (ImportProcessingProgress batchProgress in progressChannel.Reader.ReadAllAsync(cancellationToken))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException("Import job is canceled by user.");
                    }

                    _importResult.SucceedCount = batchProgress.SucceedImportCount + succeedImportCount;
                    _importResult.FailedCount = batchProgress.FailedImportCount + failedImportCount;
                    _importResult.CurrentIndex = batchProgress.CurrentIndex;

                    _logger.LogInformation("Import job progress: succeed {SucceedCount}, failed: {FailedCount}", _importResult.SucceedCount, _importResult.FailedCount);
                    progress.Report(JsonConvert.SerializeObject(_importResult));
                }

                // Pop up exception during load & import
                // Put import task before load task for resource channel full and blocking issue.
                try
                {
                    await importTask;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to import data.");
                    throw;
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
                    throw new RetriableJobException("Failed to load data", ex);
                }

                return JsonConvert.SerializeObject(_importResult);
            }
            catch (TaskCanceledException canceledEx)
            {
                _logger.LogInformation(canceledEx, CancelledErrorMessage);

                await CleanResourceForFailureAsync();

                ImportProcessingJobErrorResult error = new ImportProcessingJobErrorResult()
                {
                    Message = CancelledErrorMessage,
                };

                throw new JobExecutionException(canceledEx.Message, error, canceledEx);
            }
            catch (OperationCanceledException canceledEx)
            {
                _logger.LogInformation(canceledEx, "Data processing task is canceled.");

                await CleanResourceForFailureAsync();

                ImportProcessingJobErrorResult error = new ImportProcessingJobErrorResult()
                {
                    Message = CancelledErrorMessage,
                };

                throw new JobExecutionException(canceledEx.Message, error, canceledEx);
            }
            catch (RetriableJobException retriableEx)
            {
                _logger.LogInformation(retriableEx, "Error in data processing job.");

                await CleanResourceForFailureAsync();

                throw;
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "Critical error in data processing job.");

                await CleanResourceForFailureAsync();

                ImportProcessingJobErrorResult error = new ImportProcessingJobErrorResult()
                {
                    Message = ex.Message,
                };

                throw new JobExecutionException(ex.Message, error, ex);
            }
        }

        /// <summary>
        /// Try best to clean failure data.
        /// </summary>
        private async Task CleanResourceForFailureAsync()
        {
            try
            {
                await _resourceBulkImporter.CleanResourceAsync(_inputData, _importResult, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "Data processing job is canceled. Failed to clean resource.");
            }
        }

        private string GetErrorFileName()
        {
            return $"{_inputData.ResourceType}{_inputData.JobId}.ndjson";
        }
    }
}
