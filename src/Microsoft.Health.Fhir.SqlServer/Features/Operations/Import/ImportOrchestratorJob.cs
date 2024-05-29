// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Utility;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core;
using Microsoft.Health.Core.Features.Audit;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Audit;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Operations.Import.Models;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;
using Polly;
using JobStatus = Microsoft.Health.JobManagement.JobStatus;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Import
{
    [JobTypeId((int)JobType.ImportOrchestrator)]
    public class ImportOrchestratorJob : IJob
    {
        public const int BytesToRead = 10000 * 1000; // each job should handle about 10000 resources. with about 1000 bytes per resource

        private readonly IMediator _mediator;
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;
        private readonly IQueueClient _queueClient;
        private ImportTaskConfiguration _importConfiguration;
        private ILogger<ImportOrchestratorJob> _logger;
        private IIntegrationDataStoreClient _integrationDataStoreClient;
        private readonly IAuditLogger _auditLogger;
        internal const string DefaultCallerAgent = "Microsoft.Health.Fhir.Server";
        private static readonly AsyncPolicy _timeoutRetries = Policy
            .Handle<SqlException>(ex => ex.IsExecutionTimeout())
            .WaitAndRetryAsync(3, _ => TimeSpan.FromMilliseconds(RandomNumberGenerator.GetInt32(1000, 5000)));

        public ImportOrchestratorJob(
            IMediator mediator,
            RequestContextAccessor<IFhirRequestContext> contextAccessor,
            IIntegrationDataStoreClient integrationDataStoreClient,
            IQueueClient queueClient,
            IOptions<ImportTaskConfiguration> importConfiguration,
            ILoggerFactory loggerFactory,
            IAuditLogger auditLogger)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));
            EnsureArg.IsNotNull(integrationDataStoreClient, nameof(integrationDataStoreClient));
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            EnsureArg.IsNotNull(importConfiguration?.Value, nameof(importConfiguration));
            EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory));
            EnsureArg.IsNotNull(auditLogger, nameof(auditLogger));

            _mediator = mediator;
            _contextAccessor = contextAccessor;
            _integrationDataStoreClient = integrationDataStoreClient;
            _queueClient = queueClient;
            _importConfiguration = importConfiguration.Value;
            _logger = loggerFactory.CreateLogger<ImportOrchestratorJob>();
            _auditLogger = auditLogger;

            PollingPeriodSec = _importConfiguration.PollingFrequencyInSeconds;
        }

        public int PollingPeriodSec { get; set; }

        public async Task<string> ExecuteAsync(JobInfo jobInfo, CancellationToken cancellationToken)
        {
            var inputData = jobInfo.DeserializeDefinition<ImportOrchestratorJobDefinition>();
            var fhirRequestContext = new FhirRequestContext(
                    method: "Import",
                    uriString: inputData.RequestUri.ToString(),
                    baseUriString: inputData.BaseUri.ToString(),
                    correlationId: jobInfo.Id.ToString(),
                    requestHeaders: new Dictionary<string, StringValues>(),
                    responseHeaders: new Dictionary<string, StringValues>())
            {
                IsBackgroundTask = true,
            };
            _contextAccessor.RequestContext = fhirRequestContext;

            var result = new ImportOrchestratorJobResult();
            result.Request = inputData.RequestUri.ToString();
            ImportJobErrorResult errorResult = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                await ValidateResourcesAsync(inputData, cancellationToken);
                _logger.LogJobInformation(jobInfo, "Input resources validated.");

                await EnqueueProcessingJobsAsync(jobInfo, inputData, result, cancellationToken);
                _logger.LogJobInformation(jobInfo, "Registration of processing jobs completed.");
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogJobInformation(ex, jobInfo, "Import job canceled. {Message}", ex.Message);
                errorResult = new ImportJobErrorResult() { ErrorMessage = ex.Message, HttpStatusCode = HttpStatusCode.BadRequest };
                await SendNotification(JobStatus.Cancelled, jobInfo, 0, 0, result.TotalBytes, inputData.ImportMode, fhirRequestContext, _logger, _auditLogger, _mediator);
            }
            catch (IntegrationDataStoreException ex)
            {
                _logger.LogJobInformation(ex, jobInfo, "Failed to access input files.");
                errorResult = new ImportJobErrorResult() { ErrorMessage = ex.Message, HttpStatusCode = ex.StatusCode };
                await SendNotification(JobStatus.Failed, jobInfo, 0, 0, result.TotalBytes, inputData.ImportMode, fhirRequestContext, _logger, _auditLogger, _mediator);
            }
            catch (JobExecutionException ex)
            {
                errorResult = ex.Error != null ? (ImportJobErrorResult)ex.Error : new ImportJobErrorResult() { ErrorMessage = ex.Message, ErrorDetails = ex.ToString() };
                if (errorResult.HttpStatusCode == 0)
                {
                    errorResult.HttpStatusCode = HttpStatusCode.InternalServerError;
                }

                if (errorResult.HttpStatusCode == HttpStatusCode.InternalServerError)
                {
                    _logger.LogJobError(ex, jobInfo, "Failed to register processing jobs.");
                }
                else
                {
                    _logger.LogJobInformation(ex, jobInfo, "Failed to register processing jobs.");
                }

                await SendNotification(JobStatus.Failed, jobInfo, 0, 0, result.TotalBytes, inputData.ImportMode, fhirRequestContext, _logger, _auditLogger, _mediator);
            }
            catch (Exception ex)
            {
                _logger.LogJobError(ex, jobInfo, "Failed to register processing jobs.");
                errorResult = new ImportJobErrorResult() { ErrorMessage = ex.Message, ErrorDetails = ex.ToString(), HttpStatusCode = HttpStatusCode.InternalServerError };
                await SendNotification(JobStatus.Failed, jobInfo, 0, 0, result.TotalBytes, inputData.ImportMode, fhirRequestContext, _logger, _auditLogger, _mediator);
            }

            if (errorResult != null)
            {
                throw new JobExecutionException(errorResult.ErrorMessage, errorResult);
            }

            return JsonConvert.SerializeObject(result);
        }

        private async Task ValidateResourcesAsync(ImportOrchestratorJobDefinition inputData, CancellationToken cancellationToken)
        {
            await Parallel.ForEachAsync(inputData.Input, new ParallelOptions { MaxDegreeOfParallelism = 16 }, async (input, cancel) =>
            {
                Dictionary<string, object> properties = await _integrationDataStoreClient.GetPropertiesAsync(input.Url, cancellationToken);
                if (!string.IsNullOrEmpty(input.Etag))
                {
                    if (!input.Etag.Equals(properties[IntegrationDataStoreClientConstants.BlobPropertyETag]))
                    {
                        var errorMessage = string.Format("Input file Etag not match. {0}", input.Url);
                        var errorResult = new ImportJobErrorResult { ErrorMessage = errorMessage, HttpStatusCode = HttpStatusCode.BadRequest };
                        throw new JobExecutionException(errorMessage, errorResult);
                    }
                }
            });
        }

        internal static async Task SendNotification<T>(JobStatus status, JobInfo info, long succeeded, long failed, long bytes, ImportMode importMode, FhirRequestContext context, ILogger<T> logger, IAuditLogger auditLogger, IMediator mediator)
        {
            logger.LogJobInformation(info, "SucceededResources {SucceededResources} and FailedResources {FailedResources} in Import", succeeded, failed);

            if (importMode == ImportMode.IncrementalLoad)
            {
                var incrementalImportProperties = new Dictionary<string, string>();
                incrementalImportProperties["JobId"] = info.Id.ToString();
                incrementalImportProperties["SucceededResources"] = succeeded.ToString();
                incrementalImportProperties["FailedResources"] = failed.ToString();

                auditLogger.LogAudit(
                    AuditAction.Executed,
                    operation: "import/" + ImportMode.IncrementalLoad.ToString(),
                    resourceType: string.Empty,
                    requestUri: context.Uri,
                    statusCode: HttpStatusCode.Accepted,
                    correlationId: context.CorrelationId,
                    callerIpAddress: null,
                    callerClaims: null,
                    customHeaders: null,
                    operationType: string.Empty,
                    callerAgent: DefaultCallerAgent,
                    additionalProperties: incrementalImportProperties);

                logger.LogJobInformation(info, "Audit logs for incremental import are added.");
            }

            var importJobMetricsNotification = new ImportJobMetricsNotification(
                info.Id.ToString(),
                status.ToString(),
                info.CreateDate,
                Clock.UtcNow,
                bytes,
                succeeded,
                failed,
                importMode);

            await mediator.Publish(importJobMetricsNotification, CancellationToken.None);
        }

        private async Task EnqueueProcessingJobsAsync(JobInfo coord, ImportOrchestratorJobDefinition coordDefinition, ImportOrchestratorJobResult result, CancellationToken cancellationToken)
        {
            // split blobs by size
            var inputs = new List<InputResource>();
            await Parallel.ForEachAsync(coordDefinition.Input, new ParallelOptions { MaxDegreeOfParallelism = 16 }, async (input, cancel) =>
            {
                var blobLength = (long)(await _integrationDataStoreClient.GetPropertiesAsync(input.Url, cancellationToken))[IntegrationDataStoreClientConstants.BlobPropertyLength];
                result.TotalBytes += blobLength;
                foreach (var offset in GetOffsets(blobLength, BytesToRead))
                {
                    var newInput = input.Clone();
                    newInput.Offset = offset;
                    newInput.BytesToRead = BytesToRead;
                    lock (inputs)
                    {
                        inputs.Add(newInput);
                    }
                }
            });

            var jobIds = await EnqueueProcessingJobsAsync(inputs, coord.GroupId, coordDefinition, cancellationToken);

            result.CreatedJobs = jobIds.Count;
        }

        internal static IEnumerable<long> GetOffsets(long blobLength, int bytesToRead)
        {
            var numberOfStreams = (int)Math.Ceiling((double)blobLength / bytesToRead);
            numberOfStreams = numberOfStreams == 0 ? 1 : numberOfStreams; // record blob even if it is empty
            for (var stream = 0; stream < numberOfStreams; stream++)
            {
                yield return (long)stream * bytesToRead; // make sure that arithmetic on long is used
            }
        }

        private async Task<IList<long>> EnqueueProcessingJobsAsync(IEnumerable<InputResource> inputs, long groupId, ImportOrchestratorJobDefinition coordDefinition, CancellationToken cancellationToken)
        {
            var definitions = new List<ImportProcessingJobDefinition>();
            foreach (var input in inputs.OrderBy(_ => RandomNumberGenerator.GetInt32((int)1e9)))
            {
                var importJobPayload = new ImportProcessingJobDefinition()
                {
                    TypeId = (int)JobType.ImportProcessing,
                    ResourceLocation = input.Url.ToString(),
                    Offset = input.Offset,
                    BytesToRead = input.BytesToRead,
                    UriString = coordDefinition.RequestUri.ToString(),
                    BaseUriString = coordDefinition.BaseUri.ToString(),
                    ResourceType = input.Type,
                    GroupId = groupId,
                    ImportMode = coordDefinition.ImportMode,
                };

                definitions.Add(importJobPayload);
            }

            var orchestratorInfo = new JobInfo() { GroupId = groupId, Id = groupId };
            try
            {
                var jobIds = await _timeoutRetries.ExecuteAsync(async () => (await _queueClient.EnqueueAsync(QueueType.Import, cancellationToken, groupId: groupId, definitions: definitions.ToArray())).Select(x => x.Id).OrderBy(x => x).ToList());
                return jobIds;
            }
            catch (Exception ex)
            {
                _logger.LogJobError(ex, orchestratorInfo, "Failed to enqueue jobs.");
                throw new JobExecutionException("Failed to enqueue jobs.", ex);
            }
        }
    }
}
