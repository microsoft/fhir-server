// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.CosmosDb.Features.Storage;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Operations.Export;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.StoredProcedures.GetAvailableExportJobs;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Operations
{
    public sealed class CosmosFhirOperationsDataStore : IFhirOperationsDataStore
    {
        private readonly Func<IScoped<IDocumentClient>> _documentClientFactory;
        private readonly IFhirDataStoreContext _fhirDataStoreContext;
        private readonly RetryExceptionPolicyFactory _retryExceptionPolicyFactory;
        private readonly ILogger _logger;

        private readonly GetAvailableExportJobs _getAvailableExportJobs = new GetAvailableExportJobs();

        /// <summary>
        /// Initializes a new instance of the <see cref="CosmosFhirOperationsDataStore"/> class.
        /// </summary>
        /// <param name="documentClientFactory">The factory for <see cref="IDocumentClient"/>.</param>
        /// <param name="fhirDataStoreContext">The data store context.</param>
        /// <param name="retryExceptionPolicyFactory">The retry exception policy factory.</param>
        /// <param name="logger">The logger.</param>
        public CosmosFhirOperationsDataStore(
            Func<IScoped<IDocumentClient>> documentClientFactory,
            IFhirDataStoreContext fhirDataStoreContext,
            RetryExceptionPolicyFactory retryExceptionPolicyFactory,
            ILogger<CosmosFhirOperationsDataStore> logger)
        {
            EnsureArg.IsNotNull(documentClientFactory, nameof(documentClientFactory));
            EnsureArg.IsNotNull(fhirDataStoreContext, nameof(fhirDataStoreContext));
            EnsureArg.IsNotNull(retryExceptionPolicyFactory, nameof(retryExceptionPolicyFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _documentClientFactory = documentClientFactory;
            _fhirDataStoreContext = fhirDataStoreContext;
            _retryExceptionPolicyFactory = retryExceptionPolicyFactory;
            _logger = logger;
        }

        private IDocumentClient DocumentClient => _documentClientFactory().Value;

        public async Task<ExportJobOutcome> CreateExportJobAsync(ExportJobRecord jobRecord, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobRecord, nameof(jobRecord));

            var cosmosExportJob = new CosmosExportJobRecordWrapper(jobRecord);

            try
            {
                ResourceResponse<Document> result = await DocumentClient.CreateDocumentAsync(
                    _fhirDataStoreContext.CollectionUri,
                    cosmosExportJob,
                    new RequestOptions() { PartitionKey = new PartitionKey(CosmosDbExportConstants.ExportJobPartitionKey) },
                    disableAutomaticIdGeneration: true,
                    cancellationToken: cancellationToken);

                return new ExportJobOutcome(jobRecord, WeakETag.FromVersionId(result.Resource.ETag));
            }
            catch (DocumentClientException dce)
            {
                if (dce.StatusCode == HttpStatusCode.RequestEntityTooLarge)
                {
                    throw new RequestRateExceededException(dce.RetryAfter);
                }

                _logger.LogError(dce, "Unhandled Document Client Exception");
                throw;
            }
        }

        public async Task<ExportJobOutcome> GetExportJobAsync(string jobId, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrWhiteSpace(jobId);

            try
            {
                DocumentResponse<CosmosExportJobRecordWrapper> cosmosExportJobRecord = await DocumentClient.ReadDocumentAsync<CosmosExportJobRecordWrapper>(
                    UriFactory.CreateDocumentUri(_fhirDataStoreContext.DatabaseId, _fhirDataStoreContext.CollectionId, jobId),
                    new RequestOptions { PartitionKey = new PartitionKey(CosmosDbExportConstants.ExportJobPartitionKey) },
                    cancellationToken);

                var eTagHeaderValue = cosmosExportJobRecord.ResponseHeaders["ETag"];
                var outcome = new ExportJobOutcome(cosmosExportJobRecord.Document.JobRecord, WeakETag.FromVersionId(eTagHeaderValue));

                return outcome;
            }
            catch (DocumentClientException dce)
            {
                if (dce.StatusCode == HttpStatusCode.RequestEntityTooLarge)
                {
                    throw new RequestRateExceededException(dce.RetryAfter);
                }
                else if (dce.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new JobNotFoundException(string.Format(Core.Resources.JobNotFound, jobId));
                }

                _logger.LogError(dce, "Unhandled Document Client Exception");
                throw;
            }
        }

        public async Task<ExportJobOutcome> UpdateExportJobAsync(ExportJobRecord jobRecord, WeakETag eTag, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobRecord, nameof(jobRecord));

            var cosmosExportJob = new CosmosExportJobRecordWrapper(jobRecord);

            var requestOptions = new RequestOptions()
            {
                PartitionKey = new PartitionKey(CosmosDbExportConstants.ExportJobPartitionKey),
            };

            // Create access condition so that record is replaced only if eTag matches.
            if (eTag != null)
            {
                requestOptions.AccessCondition = new AccessCondition()
                {
                    Type = AccessConditionType.IfMatch,
                    Condition = eTag.VersionId,
                };
            }

            try
            {
                ResourceResponse<Document> replaceResult = await DocumentClient.ReplaceDocumentAsync(
                    UriFactory.CreateDocumentUri(_fhirDataStoreContext.DatabaseId, _fhirDataStoreContext.CollectionId, jobRecord.Id),
                    cosmosExportJob,
                    requestOptions,
                    cancellationToken: cancellationToken);

                var latestETag = replaceResult.Resource.ETag;
                return new ExportJobOutcome(jobRecord, WeakETag.FromVersionId(latestETag));
            }
            catch (DocumentClientException dce)
            {
                if (dce.StatusCode == HttpStatusCode.RequestEntityTooLarge)
                {
                    throw new RequestRateExceededException(dce.RetryAfter);
                }
                else if (dce.StatusCode == HttpStatusCode.PreconditionFailed)
                {
                    throw new JobConflictException();
                }
                else if (dce.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new JobNotFoundException(string.Format(Core.Resources.JobNotFound, jobRecord.Id));
                }

                _logger.LogError(dce, "Unhandled Document Client Exception");
                throw;
            }
        }

        public async Task<IReadOnlyCollection<ExportJobOutcome>> GetAvailableExportJobsAsync(
            ushort maximumNumberOfConcurrentJobsAllowed,
            TimeSpan jobHeartbeatTimeoutThreshold,
            CancellationToken cancellationToken)
        {
            try
            {
                StoredProcedureResponse<IReadOnlyCollection<CosmosExportJobRecordWrapper>> response = await _retryExceptionPolicyFactory.CreateRetryPolicy().ExecuteAsync(
                    async ct => await _getAvailableExportJobs.ExecuteAsync(
                        DocumentClient,
                        _fhirDataStoreContext.CollectionUri,
                        maximumNumberOfConcurrentJobsAllowed,
                        (ushort)jobHeartbeatTimeoutThreshold.TotalSeconds,
                        ct),
                    cancellationToken);

                return response.Response.Select(wrapper => new ExportJobOutcome(wrapper.JobRecord, WeakETag.FromVersionId(wrapper.ETag))).ToList();
            }
            catch (DocumentClientException dce)
            {
                if (dce.StatusCode == HttpStatusCode.RequestEntityTooLarge)
                {
                    throw new RequestRateExceededException(dce.RetryAfter);
                }

                _logger.LogError(dce, "Unhandled Document Client Exception");
                throw;
            }
        }
    }
}
