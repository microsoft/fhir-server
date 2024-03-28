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
using MediatR;
using Microsoft.AspNetCore.Components.Forms;
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
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Import
{
    [JobTypeId((int)JobType.ImportProcessing)]
    public class ImportProcessingJob : IJob
    {
        private const string CancelledErrorMessage = "Import processing job is canceled.";
        internal const string DefaultCallerAgent = "Microsoft.Health.Fhir.Server";

        private readonly IMediator _mediator;
        private readonly IImportResourceLoader _importResourceLoader;
        private readonly IImporter _importer;
        private readonly IImportErrorStoreFactory _importErrorStoreFactory;
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;
        private readonly ILogger<ImportProcessingJob> _logger;
        private readonly IAuditLogger _auditLogger;

        public ImportProcessingJob(
            IMediator mediator,
            IImportResourceLoader importResourceLoader,
            IImporter importer,
            IImportErrorStoreFactory importErrorStoreFactory,
            RequestContextAccessor<IFhirRequestContext> contextAccessor,
            ILoggerFactory loggerFactory,
            IAuditLogger auditLogger)
        {
            _mediator = EnsureArg.IsNotNull(mediator, nameof(mediator));
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

                // Design of error writes is too complex. We do not need separate init and writes. Also, it leads to adding duplicate error records on job restart.
                IImportErrorStore importErrorStore = await _importErrorStoreFactory.InitializeAsync(GetErrorFileName(definition.ResourceType, jobInfo.GroupId, jobInfo.Id), cancellationToken);
                currentResult.ErrorLogLocation = importErrorStore.ErrorFileLocation;

                // Design of resource loader is too complex. There is no need to have any channel and separate load task.
                // This design was driven from assumption that worker/processing job deals with entire large file.
                // This is not true anymore, as worker deals with just small portion of file accessing it by offset.
                // We should just open reader and walk through all needed records in a single thread.
                (Channel<ImportResource> importResourceChannel, Task loadTask) = _importResourceLoader.LoadResources(definition.ResourceLocation, definition.Offset, definition.BytesToRead, definition.ResourceType, definition.ImportMode, cancellationToken);

                // Import to data store
                try
                {
                    var importProgress = await _importer.Import(importResourceChannel, importErrorStore, definition.ImportMode, cancellationToken);

                    currentResult.SucceededResources = importProgress.SucceededResources;
                    currentResult.FailedResources = importProgress.FailedResources;
                    currentResult.ErrorLogLocation = importErrorStore.ErrorFileLocation; // I don't see a point of providing error file location when there are no errors.
                    currentResult.ProcessedBytes = importProgress.ProcessedBytes;

                    _logger.LogJobInformation(jobInfo, "Import Job {JobId} progress: succeed {SucceedCount}, failed: {FailedCount}", jobInfo.Id, currentResult.SucceededResources, currentResult.FailedResources);
                }
                catch (Exception ex)
                {
                    _logger.LogJobError(ex, jobInfo, "Failed to import data.");
                    throw;
                }

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
                    var error = new ImportProcessingJobErrorResult() { Message = "Due to unauthorized request, import processing operation failed." };
                    throw new JobExecutionException(ex.Message, error, ex);
                }
                catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
                {
                    _logger.LogJobInformation(ex, jobInfo, "Input file deleted, renamed, or moved during job. Import processing operation failed.");
                    var error = new ImportProcessingJobErrorResult() { Message = "Input file deleted, renamed, or moved during job. Import processing operation failed." };
                    throw new JobExecutionException(ex.Message, error, ex);
                }
                catch (Exception ex)
                {
                    _logger.LogJobError(ex, jobInfo, "RetriableJobException. Generic exception. Failed to load data.");
                    throw new RetriableJobException("Failed to load data", ex);
                }

                jobInfo.Data = currentResult.SucceededResources + currentResult.FailedResources;

                ////await SendImportMetricsNotification(jobInfo, currentResult, definition.ImportMode, fhirRequestContext);

                return JsonConvert.SerializeObject(currentResult);
            }
            catch (TaskCanceledException canceledEx)
            {
                _logger.LogJobInformation(canceledEx, jobInfo, CancelledErrorMessage);
                var error = new ImportProcessingJobErrorResult() { Message = CancelledErrorMessage };
                throw new JobExecutionException(canceledEx.Message, error, canceledEx);
            }
            catch (OperationCanceledException canceledEx)
            {
                _logger.LogJobInformation(canceledEx, jobInfo, "Import processing operation is canceled.");
                var error = new ImportProcessingJobErrorResult() { Message = CancelledErrorMessage };
                throw new JobExecutionException(canceledEx.Message, error, canceledEx);
            }
            catch (RetriableJobException retriableEx)
            {
                _logger.LogJobInformation(retriableEx, jobInfo, "Error in import processing job.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogJobInformation(ex, jobInfo, "Critical error in import processing job.");
                var error = new ImportProcessingJobErrorResult() { Message = ex.Message, Details = ex.ToString() };
                throw new JobExecutionException(ex.Message, error, ex);
            }
        }

        private async Task SendImportMetricsNotification(JobInfo jobInfo, ImportProcessingJobResult currentResult, ImportMode importMode, FhirRequestContext fhirRequestContext)
        {
            _logger.LogJobInformation(jobInfo, "SucceededResources {SucceededResources} and FailedResources {FailedResources} in Import", currentResult.SucceededResources, currentResult.FailedResources);

            if (importMode == ImportMode.IncrementalLoad)
            {
                var incrementalImportProperties = new Dictionary<string, string>();
                incrementalImportProperties["JobId"] = jobInfo.Id.ToString();
                incrementalImportProperties["SucceededResources"] = currentResult.SucceededResources.ToString();
                incrementalImportProperties["FailedResources"] = currentResult.FailedResources.ToString();

                _auditLogger.LogAudit(
                    AuditAction.Executed,
                    operation: "import/" + ImportMode.IncrementalLoad.ToString(),
                    resourceType: string.Empty,
                    requestUri: fhirRequestContext.Uri,
                    statusCode: HttpStatusCode.Accepted,
                    correlationId: fhirRequestContext.CorrelationId,
                    callerIpAddress: null,
                    callerClaims: null,
                    customHeaders: null,
                    operationType: string.Empty,
                    callerAgent: DefaultCallerAgent,
                    additionalProperties: incrementalImportProperties);

                _logger.LogJobInformation(jobInfo, "Audit logs for incremental import are added.");
            }

            var importJobMetricsNotification = new ImportJobMetricsNotification(
                jobInfo.Id.ToString(),
                JobStatus.Completed.ToString(),
                jobInfo.CreateDate,
                Clock.UtcNow,
                currentResult.ProcessedBytes,
                currentResult.SucceededResources,
                currentResult.FailedResources,
                importMode);

            await _mediator.Publish(importJobMetricsNotification, CancellationToken.None);
        }

        private static string GetErrorFileName(string resourceType, long groupId, long jobId)
        {
            return $"{resourceType}{groupId}_{jobId}.ndjson"; // jobId instead of surrogate id
        }
    }
}
