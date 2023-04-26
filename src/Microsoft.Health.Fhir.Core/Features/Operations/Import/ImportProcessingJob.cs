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
    [JobTypeId((int)JobType.ImportProcessing)]
    public class ImportProcessingJob : IJob
    {
        private const string CancelledErrorMessage = "Data processing job is canceled.";

        private readonly IImportResourceLoader _importResourceLoader;
        private readonly IImporter _resourceBulkImporter;
        private readonly IImportErrorStoreFactory _importErrorStoreFactory;
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;
        private readonly ILogger<ImportProcessingJob> _logger;

        public ImportProcessingJob(
            IImportResourceLoader importResourceLoader,
            IImporter resourceBulkImporter,
            IImportErrorStoreFactory importErrorStoreFactory,
            RequestContextAccessor<IFhirRequestContext> contextAccessor,
            ILoggerFactory loggerFactory)
        {
            EnsureArg.IsNotNull(importResourceLoader, nameof(importResourceLoader));
            EnsureArg.IsNotNull(resourceBulkImporter, nameof(resourceBulkImporter));
            EnsureArg.IsNotNull(importErrorStoreFactory, nameof(importErrorStoreFactory));
            EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));
            EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory));

            _importResourceLoader = importResourceLoader;
            _resourceBulkImporter = resourceBulkImporter;
            _importErrorStoreFactory = importErrorStoreFactory;
            _contextAccessor = contextAccessor;

            _logger = loggerFactory.CreateLogger<ImportProcessingJob>();
        }

        public async Task<string> ExecuteAsync(JobInfo jobInfo, IProgress<string> progress, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));
            EnsureArg.IsNotNull(progress, nameof(progress));

            ImportProcessingJobDefinition inputData = JsonConvert.DeserializeObject<ImportProcessingJobDefinition>(jobInfo.Definition);
            ImportProcessingJobResult currentResult = string.IsNullOrEmpty(jobInfo.Result) ? new ImportProcessingJobResult() : JsonConvert.DeserializeObject<ImportProcessingJobResult>(jobInfo.Result);

            var fhirRequestContext = new FhirRequestContext(
                    method: "Import",
                    uriString: inputData.UriString,
                    baseUriString: inputData.BaseUriString,
                    correlationId: inputData.JobId, // TODO: Replace by group id in stage 2
                    requestHeaders: new Dictionary<string, StringValues>(),
                    responseHeaders: new Dictionary<string, StringValues>())
            {
                IsBackgroundTask = true,
            };

            _contextAccessor.RequestContext = fhirRequestContext;

            long succeedImportCount = currentResult.SucceedCount;
            long failedImportCount = currentResult.FailedCount;

            currentResult.ResourceType = inputData.ResourceType;
            currentResult.ResourceLocation = inputData.ResourceLocation;
            progress.Report(JsonConvert.SerializeObject(currentResult));

            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException();
                }

                Func<long, long> sequenceIdGenerator = inputData.EndSequenceId == 0 ? (index) => 0 : (index) => inputData.BeginSequenceId + index;

                // Initialize error store
                IImportErrorStore importErrorStore = await _importErrorStoreFactory.InitializeAsync(GetErrorFileName(inputData.ResourceType, jobInfo.GroupId, jobInfo.Id), cancellationToken);
                currentResult.ErrorLogLocation = importErrorStore.ErrorFileLocation;

                // Load and parse resource from bulk resource
                (Channel<ImportResource> importResourceChannel, Task loadTask) = _importResourceLoader.LoadResources(inputData.ResourceLocation, inputData.Offset, inputData.BytesToRead, currentResult.CurrentIndex, inputData.ResourceType, sequenceIdGenerator, cancellationToken, inputData.EndSequenceId == 0);

                // Import to data store
                (Channel<ImportProcessingProgress> progressChannel, Task importTask) = _resourceBulkImporter.Import(importResourceChannel, importErrorStore, cancellationToken);

                // Update progress for checkpoints
                await foreach (ImportProcessingProgress batchProgress in progressChannel.Reader.ReadAllAsync(cancellationToken))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException("Import job is canceled by user.");
                    }

                    currentResult.SucceedCount = batchProgress.SucceedImportCount + succeedImportCount;
                    currentResult.FailedCount = batchProgress.FailedImportCount + failedImportCount;
                    currentResult.CurrentIndex = batchProgress.CurrentIndex;

                    _logger.LogInformation("Import job progress: succeed {SucceedCount}, failed: {FailedCount}", currentResult.SucceedCount, currentResult.FailedCount);
                    progress.Report(JsonConvert.SerializeObject(currentResult));
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

                jobInfo.Data = currentResult.SucceedCount + currentResult.FailedCount;

                return JsonConvert.SerializeObject(currentResult);
            }
            catch (TaskCanceledException canceledEx)
            {
                _logger.LogInformation(canceledEx, CancelledErrorMessage);
                ImportProcessingJobErrorResult error = new ImportProcessingJobErrorResult()
                {
                    Message = CancelledErrorMessage,
                };
                throw new JobExecutionException(canceledEx.Message, error, canceledEx);
            }
            catch (OperationCanceledException canceledEx)
            {
                _logger.LogInformation(canceledEx, "Data processing task is canceled.");
                ImportProcessingJobErrorResult error = new ImportProcessingJobErrorResult()
                {
                    Message = CancelledErrorMessage,
                };
                throw new JobExecutionException(canceledEx.Message, error, canceledEx);
            }
            catch (RetriableJobException retriableEx)
            {
                _logger.LogInformation(retriableEx, "Error in data processing job.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "Critical error in data processing job.");
                ImportProcessingJobErrorResult error = new ImportProcessingJobErrorResult()
                {
                    Message = ex.Message,
                };
                throw new JobExecutionException(ex.Message, error, ex);
            }
        }

        private static string GetErrorFileName(string resourceType, long groupId, long jobId)
        {
            return $"{resourceType}{groupId}_{jobId}.ndjson"; // jobId instead of resources surrogate id
        }
    }
}
