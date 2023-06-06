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
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.CosmosDb.Configs;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Operations.Export;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.StoredProcedures.AcquireExportJobs;
using Microsoft.Health.JobManagement;
using JobConflictException = Microsoft.Health.Fhir.Core.Features.Operations.JobConflictException;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Operations
{
    public sealed class CosmosFhirOperationDataStore : FhirOperationDataStoreBase, ILegacyExportOperationDataStore
    {
        private static readonly string CheckActiveJobsByStatusQuery =
            $"SELECT TOP 1 * FROM ROOT r WHERE r.{JobRecordProperties.JobRecord}.{JobRecordProperties.Status} IN ('{OperationStatus.Queued}', '{OperationStatus.Running}', '{OperationStatus.Paused}')";

        private readonly IScoped<Container> _containerScope;
        private readonly RetryExceptionPolicyFactory _retryExceptionPolicyFactory;
        private readonly ICosmosQueryFactory _queryFactory;
        private readonly ILogger _logger;

        private static readonly AcquireExportJobs _acquireExportJobs = new();

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

            DatabaseId = cosmosDataStoreConfiguration.DatabaseId;
            CollectionId = collectionConfiguration.CollectionId;
        }

        private string DatabaseId { get; }

        private string CollectionId { get; }

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

        public override async Task<ExportJobOutcome> UpdateExportJobAsync(ExportJobRecord jobRecord, WeakETag eTag, CancellationToken cancellationToken)
        {
            if (IsLegacyJob(jobRecord.Id))
            {
                // try old job records
                var oldJobs = (ILegacyExportOperationDataStore)this;
                return await oldJobs.UpdateLegacyExportJobAsync(jobRecord, eTag, cancellationToken);
            }

            return await base.UpdateExportJobAsync(jobRecord, eTag, cancellationToken);
        }

        private static bool IsLegacyJob(string jobId)
        {
            return !long.TryParse(jobId, out long _);
        }

        async Task<IReadOnlyCollection<ExportJobOutcome>> ILegacyExportOperationDataStore.AcquireLegacyExportJobsAsync(
            ushort numberOfJobsToAcquire,
            TimeSpan jobHeartbeatTimeoutThreshold,
            CancellationToken cancellationToken)
        {
            try
            {
                var response = await _retryExceptionPolicyFactory.RetryPolicy.ExecuteAsync(
                    async ct => await _acquireExportJobs.ExecuteAsync(
                        _containerScope.Value.Scripts,
                        numberOfJobsToAcquire,
                        (ushort)jobHeartbeatTimeoutThreshold.TotalSeconds,
                        ct),
                    cancellationToken);

                return response.Resource.Select(wrapper => new ExportJobOutcome(wrapper.JobRecord, WeakETag.FromVersionId(wrapper.ETag))).ToList();
            }
            catch (CosmosException dce)
            {
                if (dce.IsRequestEntityTooLarge())
                {
                    throw;
                }

                _logger.LogError(dce, "Failed to acquire export jobs.");
                throw;
            }
        }

        async Task<ExportJobOutcome> ILegacyExportOperationDataStore.GetLegacyExportJobByIdAsync(string id, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrWhiteSpace(id, nameof(id));

            try
            {
                ItemResponse<CosmosExportJobRecordWrapper> cosmosExportJobRecord = await _containerScope.Value.ReadItemAsync<CosmosExportJobRecordWrapper>(
                    id,
                    new PartitionKey(CosmosDbExportConstants.ExportJobPartitionKey),
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
                    throw new JobNotFoundException(string.Format(Core.Resources.JobNotFound, id));
                }

                _logger.LogError(dce, "Failed to get an export job by id.");
                throw;
            }
        }

        async Task<ExportJobOutcome> ILegacyExportOperationDataStore.UpdateLegacyExportJobAsync(ExportJobRecord jobRecord, WeakETag eTag, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobRecord, nameof(jobRecord));

            var cosmosExportJob = new CosmosExportJobRecordWrapper(jobRecord);
            var requestOptions = new ItemRequestOptions();

            // Create access condition so that record is replaced only if eTag matches.
            if (eTag != null)
            {
                requestOptions.IfMatchEtag = eTag.VersionId;
            }

            try
            {
                var replaceResult = await _retryExceptionPolicyFactory.RetryPolicy.ExecuteAsync(
                    () => _containerScope.Value.ReplaceItemAsync(
                        cosmosExportJob,
                        jobRecord.Id,
                        new PartitionKey(CosmosDbExportConstants.ExportJobPartitionKey),
                        cancellationToken: cancellationToken,
                        requestOptions: requestOptions));

                var latestETag = replaceResult.Resource.ETag;
                return new ExportJobOutcome(jobRecord, WeakETag.FromVersionId(latestETag));
            }
            catch (CosmosException dce)
            {
                if (dce.IsRequestRateExceeded())
                {
                    throw;
                }
                else if (dce.StatusCode == HttpStatusCode.PreconditionFailed)
                {
                    throw new JobConflictException();
                }
                else if (dce.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new JobNotFoundException(string.Format(Core.Resources.JobNotFound, jobRecord.Id));
                }

                _logger.LogError(dce, "Failed to update an export job.");
                throw;
            }
        }

        public override async Task<IReadOnlyCollection<ExportJobOutcome>> AcquireExportJobsAsync(
            ushort numberOfJobsToAcquire,
            TimeSpan jobHeartbeatTimeoutThreshold,
            CancellationToken cancellationToken)
        {
            try
            {
                var response = await _retryExceptionPolicyFactory.RetryPolicy.ExecuteAsync(
                    async ct => await _acquireExportJobs.ExecuteAsync(
                        _containerScope.Value.Scripts,
                        numberOfJobsToAcquire,
                        (ushort)jobHeartbeatTimeoutThreshold.TotalSeconds,
                        ct),
                    cancellationToken);

                return response.Resource.Select(wrapper => new ExportJobOutcome(wrapper.JobRecord, WeakETag.FromVersionId(wrapper.ETag))).ToList();
            }
            catch (CosmosException dce)
            {
                if (dce.IsRequestEntityTooLarge())
                {
                    throw;
                }

                _logger.LogError(dce, "Failed to acquire export jobs.");
                throw;
            }
        }
    }
}
