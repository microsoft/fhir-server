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
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.CosmosDb.Core.Configs;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Operations.LegacyExport;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Operations.Reindex;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;
using JobConflictException = Microsoft.Health.Fhir.Core.Features.Operations.JobConflictException;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Operations
{
    public sealed class CosmosFhirOperationDataStore : FhirOperationDataStoreBase, ILegacyExportOperationDataStore, ILegacyReindexOperationDataStore
    {
        private readonly IScoped<Container> _containerScope;
        private readonly RetryExceptionPolicyFactory _retryExceptionPolicyFactory;
        private readonly ICosmosQueryFactory _queryFactory;
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="CosmosFhirOperationDataStore"/> class.
        /// </summary>
        /// <param name="queueClient">The QueueClient.</param>
        /// <param name="containerScope">The factory for <see cref="Container"/>.</param>
        /// <param name="cosmosDataStoreConfiguration">The data store configuration.</param>
        /// <param name="namedCosmosCollectionConfigurationAccessor">The IOptions accessor to get a named version.</param>
        /// <param name="retryExceptionPolicyFactory">The retry exception policy factory.</param>
        /// <param name="queryFactory">The Query Factory</param>
        /// <param name="logger">The logger.</param>
        /// <param name="loggerFactory">The Logger Factory.</param>
        public CosmosFhirOperationDataStore(
            IQueueClient queueClient,
            IScoped<Container> containerScope,
            CosmosDataStoreConfiguration cosmosDataStoreConfiguration,
            IOptionsMonitor<CosmosCollectionConfiguration> namedCosmosCollectionConfigurationAccessor,
            RetryExceptionPolicyFactory retryExceptionPolicyFactory,
            ICosmosQueryFactory queryFactory,
            ILogger<CosmosFhirOperationDataStore> logger,
            ILoggerFactory loggerFactory)
            : base(queueClient, loggerFactory)
        {
            EnsureArg.IsNotNull(containerScope, nameof(containerScope));
            EnsureArg.IsNotNull(cosmosDataStoreConfiguration, nameof(cosmosDataStoreConfiguration));
            EnsureArg.IsNotNull(namedCosmosCollectionConfigurationAccessor, nameof(namedCosmosCollectionConfigurationAccessor));
            EnsureArg.IsNotNull(retryExceptionPolicyFactory, nameof(retryExceptionPolicyFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _containerScope = containerScope;
            _retryExceptionPolicyFactory = retryExceptionPolicyFactory;
            _queryFactory = queryFactory;
            _logger = logger;

            CosmosCollectionConfiguration collectionConfiguration = namedCosmosCollectionConfigurationAccessor.Get(Constants.CollectionConfigurationName);
        }

        public override async Task<ExportJobOutcome> GetExportJobByIdAsync(string id, CancellationToken cancellationToken)
        {
            if (IsLegacyJob(id))
            {
                // try old job records
                var oldJobs = (ILegacyExportOperationDataStore)this;
                return await oldJobs.GetLegacyExportJobByIdAsync(id, cancellationToken);
            }

            return await base.GetExportJobByIdAsync(id, cancellationToken);
        }

        private static bool IsLegacyJob(string jobId)
        {
            return !long.TryParse(jobId, out long _);
        }

        async Task<ExportJobOutcome> ILegacyExportOperationDataStore.GetLegacyExportJobByIdAsync(string id, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrWhiteSpace(id, nameof(id));

            try
            {
                ItemResponse<CosmosLegacyExportJobRecordWrapper> cosmosExportJobRecord = await _containerScope.Value.ReadItemAsync<CosmosLegacyExportJobRecordWrapper>(
                    id,
                    new PartitionKey(CosmosDbLegacyExportConstants.ExportJobPartitionKey),
                    cancellationToken: cancellationToken);

                var outcome = new ExportJobOutcome(cosmosExportJobRecord.Resource.JobRecord, WeakETag.FromVersionId(cosmosExportJobRecord.Resource.ETag));

                return outcome;
            }
            catch (CosmosException dce)
            {
                if (dce.IsRequestRateExceeded())
                {
                    throw;
                }

                if (dce.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new JobNotFoundException(string.Format(Microsoft.Health.Fhir.Core.Resources.JobNotFound, id));
                }

                _logger.LogError(dce, "Failed to get an export job by id.");
                throw;
            }
        }

        public override async Task<ReindexJobWrapper> GetReindexJobByIdAsync(string jobId, CancellationToken cancellationToken)
        {
            if (IsLegacyJob(jobId))
            {
                // try old job records
                var oldJobs = (ILegacyReindexOperationDataStore)this;
                return await oldJobs.GetLegacyReindexJobByIdAsync(jobId, cancellationToken);
            }

            return await base.GetReindexJobByIdAsync(jobId, cancellationToken);
        }

        async Task<ReindexJobWrapper> ILegacyReindexOperationDataStore.GetLegacyReindexJobByIdAsync(string jobId, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrWhiteSpace(jobId, nameof(jobId));

            try
            {
                var cosmosReindexJobRecord = await _containerScope.Value.ReadItemAsync<CosmosReindexJobRecordWrapper>(
                    jobId,
                    new PartitionKey(CosmosDbReindexConstants.ReindexJobPartitionKey),
                    cancellationToken: cancellationToken);

                var outcome = new ReindexJobWrapper(
                    cosmosReindexJobRecord.Resource.JobRecord,
                    WeakETag.FromVersionId(cosmosReindexJobRecord.Resource.ETag));

                return outcome;
            }
            catch (CosmosException dce)
            {
                if (dce.IsRequestRateExceeded())
                {
                    throw;
                }
                else if (dce.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new JobNotFoundException(string.Format(Microsoft.Health.Fhir.Core.Resources.JobNotFound, jobId));
                }

                _logger.LogError(dce, "Failed to get legacy reindex job by id: {JobId}.", jobId);
                throw;
            }
        }
    }
}
