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

            var definition = JsonConvert.DeserializeObject<ImportProcessingJobDefinition>(jobInfo.Definition);
            var currentResult = new ImportProcessingJobResult();

            var fhirRequestContext = new FhirRequestContext(
                    method: "Import",
                    uriString: definition.UriString,
                    baseUriString: definition.BaseUriString,
                    correlationId: definition.JobId, // TODO: Replace by group id in stage 2
                    requestHeaders: new Dictionary<string, StringValues>(),
                    responseHeaders: new Dictionary<string, StringValues>())
            {
                IsBackgroundTask = true,
            };

            _contextAccessor.RequestContext = fhirRequestContext;

            progress.Report(JsonConvert.SerializeObject(currentResult));

            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException();
                }

                Func<long, long> sequenceIdGenerator = definition.EndSequenceId == 0 ? (index) => 0 : (index) => definition.BeginSequenceId + index;

                // Initialize error store
                IImportErrorStore importErrorStore = await _importErrorStoreFactory.InitializeAsync(GetErrorFileName(definition.ResourceType, jobInfo.GroupId, jobInfo.Id), cancellationToken);
                currentResult.ErrorLogLocation = importErrorStore.ErrorFileLocation;

                // Load and parse resource from bulk resource
                (Channel<ImportResource> importResourceChannel, Task loadTask) = _importResourceLoader.LoadResources(definition.ResourceLocation, definition.Offset, definition.BytesToRead, currentResult.CurrentIndex, definition.ResourceType, sequenceIdGenerator, cancellationToken, definition.EndSequenceId == 0);

                // Import to data store
                try
                {
                    var importProgress = await _resourceBulkImporter.Import(importResourceChannel, importErrorStore, cancellationToken);

                    currentResult.SucceededResources = importProgress.SucceededResources;
                    currentResult.FailedResources = importProgress.FailedResources;
                    currentResult.CurrentIndex = importProgress.CurrentIndex;

                    _logger.LogInformation("Import job progress: succeed {SucceedCount}, failed: {FailedCount}", currentResult.SucceededResources, currentResult.FailedResources);
                    progress.Report(JsonConvert.SerializeObject(currentResult));
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

                jobInfo.Data = currentResult.SucceededResources + currentResult.FailedResources;

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
