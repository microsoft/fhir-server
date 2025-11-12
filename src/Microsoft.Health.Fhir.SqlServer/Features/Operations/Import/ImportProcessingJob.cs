// -------------------------------------------------------------------------------------------------
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
using Medino;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Audit;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Audit;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.JobManagement;
using Microsoft.Health.SqlServer.Features.Storage;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Import
{
    [JobTypeId((int)JobType.ImportProcessing)]
    public class ImportProcessingJob : IJob
    {
        private const string CancelledErrorMessage = "Import processing job is canceled.";
        internal const string DefaultCallerAgent = "Microsoft.Health.Fhir.Server";
        internal const string SurrogateIdsErrorMessage = "Unable to generate internal IDs. If the lastUpdated meta element is provided as input, reduce the number of resources with the same up to millisecond lastUpdated below 10,000.";

        private readonly IMediator _mediator;
        private readonly IQueueClient _queueClient;
        private readonly IImportResourceLoader _importResourceLoader;
        private readonly IImporter _importer;
        private readonly IImportErrorStoreFactory _importErrorStoreFactory;
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;
        private readonly ILogger<ImportProcessingJob> _logger;
        private readonly IAuditLogger _auditLogger;

        public ImportProcessingJob(
            IMediator mediator,
            IQueueClient queueClient,
            IImportResourceLoader importResourceLoader,
            IImporter importer,
            IImportErrorStoreFactory importErrorStoreFactory,
            RequestContextAccessor<IFhirRequestContext> contextAccessor,
            ILoggerFactory loggerFactory,
            IAuditLogger auditLogger)
        {
            _mediator = EnsureArg.IsNotNull(mediator, nameof(mediator));
            _queueClient = EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            _importResourceLoader = EnsureArg.IsNotNull(importResourceLoader, nameof(importResourceLoader));
            _importer = EnsureArg.IsNotNull(importer, nameof(importer));
            _importErrorStoreFactory = EnsureArg.IsNotNull(importErrorStoreFactory, nameof(importErrorStoreFactory));
            _contextAccessor = EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));
            _logger = EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory)).CreateLogger<ImportProcessingJob>();
            _auditLogger = EnsureArg.IsNotNull(auditLogger, nameof(auditLogger));
        }

        public async Task<string> ExecuteAsync(JobInfo jobInfo, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));

            var definition = jobInfo.DeserializeDefinition<ImportProcessingJobDefinition>();
            var result = new ImportProcessingJobResult();

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

                // Design of error writes is too complex. We do not need separate init and writes. Also, it leads to adding duplicate error records on job restart.
                IImportErrorStore importErrorStore;
                string errorFileName = GetErrorFileName(definition.ResourceType, jobInfo.GroupId, jobInfo.Id);

                if (string.IsNullOrEmpty(definition.ErrorContainerName))
                {
                    importErrorStore = await _importErrorStoreFactory.InitializeAsync(errorFileName, cancellationToken);
                }
                else
                {
                    importErrorStore = await _importErrorStoreFactory.InitializeAsync(definition.ErrorContainerName, errorFileName, cancellationToken);
                }

                result.ErrorLogLocation = importErrorStore.ErrorFileLocation;

                // Design of resource loader is too complex. There is no need to have any channel and separate load task.
                // This design was driven from assumption that worker/processing job deals with entire large file.
                // This is not true anymore, as worker deals with just small portion of file accessing it by offset.
                // We should just open reader and walk through all needed records in a single thread.
                (Channel<ImportResource> importResourceChannel, Task loadTask) = _importResourceLoader.LoadResources(definition.ResourceLocation, definition.Offset, definition.BytesToRead, definition.ResourceType, definition.ImportMode, cancellationToken);

                // Import to data store
                var importProgress = await _importer.Import(importResourceChannel, importErrorStore, definition.ImportMode, definition.AllowNegativeVersions, definition.EventualConsistency, cancellationToken);

                result.SucceededResources = importProgress.SucceededResources;
                result.FailedResources = importProgress.FailedResources;
                result.ErrorLogLocation = importErrorStore.ErrorFileLocation;
                result.ProcessedBytes = importProgress.ProcessedBytes;

                _logger.LogJobInformation(jobInfo, "Import Job {JobId} progress: succeed {SucceedCount}, failed: {FailedCount}", jobInfo.Id, result.SucceededResources, result.FailedResources);

                try
                {
                    await loadTask;
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
                    throw new JobExecutionException(ex.Message, error, ex, false);
                }
                catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
                {
                    _logger.LogJobInformation(ex, jobInfo, "Input file deleted, renamed, or moved during job. Import processing operation failed.");
                    var error = new ImportJobErrorResult() { ErrorMessage = "Input file deleted, renamed, or moved during job. Import processing operation failed.", HttpStatusCode = HttpStatusCode.BadRequest };
                    throw new JobExecutionException(ex.Message, error, ex, false);
                }
                catch (IntegrationDataStoreException ex)
                {
                    _logger.LogJobWarning(ex, jobInfo, "Failed to access input files.");
                    var error = new ImportJobErrorResult() { ErrorMessage = ex.Message, HttpStatusCode = ex.StatusCode };
                    throw new JobExecutionException(ex.Message, error, ex, false);
                }
                catch (Exception ex)
                {
                    _logger.LogJobError(ex, jobInfo, "Generic exception. Failed to load data.");
                    var error = new ImportJobErrorResult() { ErrorMessage = "Generic exception. Failed to load data." };
                    throw new JobExecutionException(ex.Message, error, ex, false);
                }

                jobInfo.Data = result.SucceededResources + result.FailedResources;

                // jobs are small, send on success only
                await ImportOrchestratorJob.SendNotification(JobStatus.Completed, jobInfo, result.SucceededResources, result.FailedResources, result.ProcessedBytes, definition.ImportMode, fhirRequestContext, _logger, _auditLogger, _mediator);

                return JsonConvert.SerializeObject(result);
            }
            catch (OperationCanceledException canceledEx)
            {
                _logger.LogJobInformation(canceledEx, jobInfo, "Import processing operation is canceled.");
                var error = new ImportJobErrorResult() { ErrorMessage = CancelledErrorMessage };
                throw new JobExecutionException(canceledEx.Message, error, canceledEx, false);
            }
            catch (SqlException ex) when (ex.Number == SqlErrorCodes.Conflict)
            {
                _logger.LogJobInformation(ex, jobInfo, "Exceeded retries on conflicts. Most likely reason - too many input resources with the same last updated.");
                var error = new ImportJobErrorResult() { ErrorMessage = SurrogateIdsErrorMessage, HttpStatusCode = HttpStatusCode.BadRequest, ErrorDetails = ex.ToString() };
                throw new JobExecutionException(ex.Message, error, ex, false);
            }
            catch (OverflowException ex)
            {
                _logger.LogJobError(ex, jobInfo, ex.Message);
                var error = new ImportJobErrorResult() { ErrorMessage = ex.Message, HttpStatusCode = HttpStatusCode.BadRequest, ErrorDetails = ex.ToString() };
                throw new JobExecutionException(ex.Message, error, ex, false);
            }
            catch (IntegrationDataStoreException ex) when (ex.Message.Contains("The specified resource name contains invalid characters", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogJobError(ex, jobInfo, ex.Message);
                var error = new ImportJobErrorResult() { ErrorMessage = "Error container name contains invalid characters. Only lowercase letters, numbers, and hyphens are allowed. The name must begin and end with a letter or a number. The name can't contain two consecutive hyphens.", HttpStatusCode = HttpStatusCode.BadRequest, ErrorDetails = ex.ToString() };
                throw new JobExecutionException(error.ErrorMessage, error, ex, false);
            }
            catch (IntegrationDataStoreException ex) when (ex.Message.Contains("The specified resource name length is not within the permissible limits", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogJobError(ex, jobInfo, ex.Message);
                var error = new ImportJobErrorResult() { ErrorMessage = "The error container name must be between 3 and 63 characters long.", HttpStatusCode = HttpStatusCode.BadRequest, ErrorDetails = ex.ToString() };
                throw new JobExecutionException(error.ErrorMessage, error, ex, false);
            }
            catch (Exception ex)
            {
                _logger.LogJobError(ex, jobInfo, "Critical error in import processing job.");
                var error = new ImportJobErrorResult() { ErrorMessage = ex.Message, ErrorDetails = ex.ToString() };
                throw new JobExecutionException(ex.Message, error, ex, false);
            }
        }

        private static string GetErrorFileName(string resourceType, long groupId, long jobId)
        {
            return $"{resourceType}{groupId}_{jobId}.ndjson"; // jobId instead of surrogate id
        }
    }
}
