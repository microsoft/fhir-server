﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Azure;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Import
{
    [JobTypeId((int)JobType.ImportProcessing)]
    public class ImportProcessingJob : IJob
    {
        private const string CancelledErrorMessage = "Import processing job is canceled.";

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
            _importResourceLoader = EnsureArg.IsNotNull(importResourceLoader, nameof(importResourceLoader));
            _importer = EnsureArg.IsNotNull(importer, nameof(importer));
            _importErrorStoreFactory = EnsureArg.IsNotNull(importErrorStoreFactory, nameof(importErrorStoreFactory));
            _contextAccessor = EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));
            _logger = EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory)).CreateLogger<ImportProcessingJob>();
        }

        public async Task<string> ExecuteAsync(JobInfo jobInfo, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));

            var definition = jobInfo.DeserializeDefinition<ImportProcessingJobDefinition>();
            var currentResult = new ImportProcessingJobResult();

            var fhirRequestContext = new FhirRequestContext(
                    method: "Import",
                    uriString: definition.UriString,
                    baseUriString: definition.BaseUriString,
                    correlationId: jobInfo.GroupId.ToString(),
                    requestHeaders: new Dictionary<string, StringValues>(),
                    responseHeaders: new Dictionary<string, StringValues>())
            {
                IsBackgroundTask = true,
            };

            _contextAccessor.RequestContext = fhirRequestContext;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Initialize error store
                IImportErrorStore importErrorStore = await _importErrorStoreFactory.InitializeAsync(GetErrorFileName(definition.ResourceType, jobInfo.GroupId, jobInfo.Id), cancellationToken);
                currentResult.ErrorLogLocation = importErrorStore.ErrorFileLocation;

                // Load and parse resource from bulk resource
                (Channel<ImportResource> importResourceChannel, Task loadTask) = _importResourceLoader.LoadResources(definition.ResourceLocation, definition.Offset, definition.BytesToRead, definition.ResourceType, definition.ImportMode, cancellationToken);

                // Import to data store
                var importProgress = await _importer.Import(importResourceChannel, importErrorStore, definition.ImportMode, cancellationToken);

                currentResult.SucceededResources = importProgress.SucceededResources;
                currentResult.FailedResources = importProgress.FailedResources;
                currentResult.ErrorLogLocation = importErrorStore.ErrorFileLocation;
                currentResult.ProcessedBytes = importProgress.ProcessedBytes;

                _logger.LogJobInformation(jobInfo, "Import Job {JobId} progress: succeed {SucceedCount}, failed: {FailedCount}", jobInfo.Id, currentResult.SucceededResources, currentResult.FailedResources);

                try
                {
                    await loadTask;
                }
                catch (TaskCanceledException tce)
                {
                    _logger.LogJobWarning(tce, jobInfo, nameof(TaskCanceledException));
                    throw;
                }
                catch (OperationCanceledException oce)
                {
                    _logger.LogJobWarning(oce, jobInfo, nameof(OperationCanceledException));
                    throw;
                }
                catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.Forbidden || ex.Status == (int)HttpStatusCode.Unauthorized)
                {
                    _logger.LogJobInformation(ex, jobInfo, "Due to unauthorized request, import processing operation failed.");
                    var error = new ImportJobErrorResult() { ErrorMessage = "Due to unauthorized request, import processing operation failed.", HttpStatusCode = HttpStatusCode.BadRequest };
                    throw new JobExecutionException(ex.Message, error, ex);
                }
                catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
                {
                    _logger.LogJobInformation(ex, jobInfo, "Input file deleted, renamed, or moved during job. Import processing operation failed.");
                    var error = new ImportJobErrorResult() { ErrorMessage = "Input file deleted, renamed, or moved during job. Import processing operation failed.", HttpStatusCode = HttpStatusCode.BadRequest };
                    throw new JobExecutionException(ex.Message, error, ex);
                }
                catch (IntegrationDataStoreException ex)
                {
                    _logger.LogJobInformation(ex, jobInfo, "Failed to access input files.");
                    var error = new ImportJobErrorResult() { ErrorMessage = ex.Message, HttpStatusCode = ex.StatusCode };
                    throw new JobExecutionException(ex.Message, error, ex);
                }
                catch (Exception ex)
                {
                    _logger.LogJobError(ex, jobInfo, "Generic exception. Failed to load data.");
                    var error = new ImportJobErrorResult() { ErrorMessage = "Generic exception. Failed to load data." };
                    throw new JobExecutionException(ex.Message, error, ex);
                }

                jobInfo.Data = currentResult.SucceededResources + currentResult.FailedResources;
                return JsonConvert.SerializeObject(currentResult);
            }
            catch (TaskCanceledException canceledEx)
            {
                _logger.LogJobInformation(canceledEx, jobInfo, CancelledErrorMessage);
                var error = new ImportJobErrorResult() { ErrorMessage = CancelledErrorMessage };
                throw new JobExecutionException(canceledEx.Message, error, canceledEx);
            }
            catch (OperationCanceledException canceledEx)
            {
                _logger.LogJobInformation(canceledEx, jobInfo, "Import processing operation is canceled.");
                var error = new ImportJobErrorResult() { ErrorMessage = CancelledErrorMessage };
                throw new JobExecutionException(canceledEx.Message, error, canceledEx);
            }
            catch (Exception ex)
            {
                _logger.LogJobInformation(ex, jobInfo, "Critical error in import processing job.");
                var error = new ImportJobErrorResult() { ErrorMessage = ex.Message, ErrorDetails = ex.ToString() };
                throw new JobExecutionException(ex.Message, error, ex);
            }
        }

        private static string GetErrorFileName(string resourceType, long groupId, long jobId)
        {
            return $"{resourceType}{groupId}_{jobId}.ndjson"; // jobId instead of surrogate id
        }
    }
}
