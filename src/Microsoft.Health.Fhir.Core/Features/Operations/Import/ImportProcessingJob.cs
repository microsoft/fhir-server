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
        private readonly IImporter _importer;
        private readonly IImportErrorStoreFactory _importErrorStoreFactory;
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;
        private readonly ILogger<ImportProcessingJob> _logger;

        public ImportProcessingJob(
            IImportResourceLoader importResourceLoader,
            IImporter importer,
            IImportErrorStoreFactory importErrorStoreFactory,
            RequestContextAccessor<IFhirRequestContext> contextAccessor,
            ILoggerFactory loggerFactory)
        {
            EnsureArg.IsNotNull(importResourceLoader, nameof(importResourceLoader));
            EnsureArg.IsNotNull(importer, nameof(importer));
            EnsureArg.IsNotNull(importErrorStoreFactory, nameof(importErrorStoreFactory));
            EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));
            EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory));

            _importResourceLoader = importResourceLoader;
            _importer = importer;
            _importErrorStoreFactory = importErrorStoreFactory;
            _contextAccessor = contextAccessor;

            _logger = loggerFactory.CreateLogger<ImportProcessingJob>();
        }

        public async Task<string> ExecuteAsync(JobInfo jobInfo, IProgress<string> progress, CancellationToken cancellationToken)
        {
            // this job is not reporting any progress, so there is no need to check what is passed
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));

            var definition = JsonConvert.DeserializeObject<ImportProcessingJobDefinition>(jobInfo.Definition);

            var fhirRequestContext = new FhirRequestContext(
                    method: "Import",
                    uriString: definition.UriString,
                    baseUriString: definition.BaseUriString,
                    correlationId: jobInfo.Id.ToString(),
                    requestHeaders: new Dictionary<string, StringValues>(),
                    responseHeaders: new Dictionary<string, StringValues>())
            {
                IsBackgroundTask = true,
            };

            _contextAccessor.RequestContext = fhirRequestContext;

            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException();
                }

                // Initialize error store
                IImportErrorStore importErrorStore = await _importErrorStoreFactory.InitializeAsync(GetErrorFileName(definition.ResourceType, jobInfo.GroupId, jobInfo.Id), cancellationToken);

                // Load and parse resource from bulk resource
                (Channel<ImportResource> importResourceChannel, Task loadTask) = _importResourceLoader.LoadResources(definition.ResourceLocation, definition.Offset, definition.BytesToRead, definition.ResourceType, cancellationToken);

                // Pop up exception during load & import
                // Put import task before load task for resource channel full and blocking issue.
                var currentResult = new ImportProcessingJobResult();
                try
                {
                    // Import to data store
                    ImportProcessingProgress processingProgress = await _importer.Import(importResourceChannel, importErrorStore, cancellationToken);

                    currentResult.SucceedCount = processingProgress.SucceedImportCount;
                    currentResult.FailedCount = processingProgress.FailedImportCount;
                    currentResult.ErrorLogLocation = importErrorStore.ErrorFileLocation;

                    _logger.LogInformation("Import job progress: succeed {SucceedCount}, failed: {FailedCount}", currentResult.SucceedCount, currentResult.FailedCount);
                }
                catch (TaskCanceledException)
                {
                    throw;
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
                var error = new ImportProcessingJobErrorResult() { Message = CancelledErrorMessage };
                throw new JobExecutionException(canceledEx.Message, error, canceledEx);
            }
            catch (OperationCanceledException canceledEx)
            {
                _logger.LogInformation(canceledEx, "Data processing task is canceled.");
                var error = new ImportProcessingJobErrorResult() { Message = CancelledErrorMessage };
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
                var error = new ImportProcessingJobErrorResult() { Message = ex.Message };
                throw new JobExecutionException(ex.Message, error, ex);
            }
        }

        private static string GetErrorFileName(string resourceType, long groupId, long jobId)
        {
            return $"{resourceType}{groupId}_{jobId}.ndjson"; // jobId instead of resources surrogate id
        }
    }
}
