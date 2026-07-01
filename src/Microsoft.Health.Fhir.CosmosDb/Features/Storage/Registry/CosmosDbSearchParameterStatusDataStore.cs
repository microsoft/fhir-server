// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Health.Core;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.CosmosDb.Core.Configs;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Registry
{
    public sealed class CosmosDbSearchParameterStatusDataStore : ISearchParameterStatusDataStore, IDisposable
    {
        private readonly Func<IScoped<Container>> _containerScopeFactory;
        private readonly ICosmosQueryFactory _queryFactory;
        private readonly IQueueClient _queueClient;
        private readonly SemaphoreSlim _statusListSemaphore = new(1, 1);

        public CosmosDbSearchParameterStatusDataStore(
            Func<IScoped<Container>> containerScopeFactory,
            CosmosDataStoreConfiguration cosmosDataStoreConfiguration,
            ICosmosQueryFactory queryFactory,
            IQueueClient queueClient)
        {
            EnsureArg.IsNotNull(containerScopeFactory, nameof(containerScopeFactory));
            EnsureArg.IsNotNull(cosmosDataStoreConfiguration, nameof(cosmosDataStoreConfiguration));
            EnsureArg.IsNotNull(queryFactory, nameof(queryFactory));
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));

            _containerScopeFactory = containerScopeFactory;
            _queryFactory = queryFactory;
            _queueClient = queueClient;
        }

        public string SearchParamCacheUpdateProcessName => null;

        public async Task TryLogEvent(string process, string status, string text, DateTime? startDate, CancellationToken cancellationToken)
        {
            await Task.CompletedTask; // noop
        }

        public async Task<IReadOnlyCollection<ResourceSearchParameterStatus>> GetSearchParameterStatuses(CancellationToken cancellationToken, DateTimeOffset? startLastUpdated = null)
        {
            using IScoped<Container> clientScope = _containerScopeFactory.Invoke();
            using var retryDelayToken = new CancellationTokenSource(TimeSpan.FromMinutes(1));
            var lastUpdated = startLastUpdated.HasValue ? startLastUpdated.Value : DateTimeOffset.MinValue;

            await _statusListSemaphore.WaitAsync(retryDelayToken.Token);
            try
            {
                var parameterStatus = new List<ResourceSearchParameterStatus>();
                do
                {
                    var query = _queryFactory.Create<SearchParameterStatusWrapper>(
                        clientScope.Value,
                        new CosmosQueryContext(
                            new QueryDefinition("select * from c"),
                            new QueryRequestOptions
                            {
                                PartitionKey = new PartitionKey(SearchParameterStatusWrapper.SearchParameterStatusPartitionKey),
                            }));
                    do
                    {
                        FeedResponse<SearchParameterStatusWrapper> results = await query.ExecuteNextAsync(cancellationToken);

                        parameterStatus.AddRange(results.Select(x => x.ToSearchParameterStatus()));
                    }
                    while (query.HasMoreResults);

                    if (!parameterStatus.Any())
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), retryDelayToken.Token);
                    }
                }
                while (!parameterStatus.Any() && !retryDelayToken.IsCancellationRequested);

                return parameterStatus.Where(_ => _.LastUpdated > lastUpdated).ToList();
            }
            finally
            {
                _statusListSemaphore.Release();
            }
        }

        public async Task UpsertStatuses(IReadOnlyList<ResourceSearchParameterStatus> statuses, CancellationToken cancellationToken, long? reindexId = null)
        {
            EnsureArg.IsNotNull(statuses, nameof(statuses));

            if (statuses.Count == 0)
            {
                return;
            }

            // Check for active reindex jobs unless this call is from a reindex job itself
            if (!reindexId.HasValue || reindexId.Value <= 0)
            {
                var activeJobs = await _queueClient.GetActiveJobsByQueueTypeAsync((byte)QueueType.Reindex, returnParentOnly: true, cancellationToken);
                if (activeJobs.Count > 0)
                {
                    var jobId = activeJobs[0].Id;
                    throw new Fhir.Core.Features.Operations.JobConflictException(string.Format(Fhir.Core.Resources.ChangesToSearchParametersNotAllowedWhileReindexing, jobId));
                }
            }

            foreach (IEnumerable<ResourceSearchParameterStatus> statusBatch in statuses.TakeBatch(100))
            {
                using IScoped<Container> clientScope = _containerScopeFactory.Invoke();
                TransactionalBatch batch = clientScope.Value.CreateTransactionalBatch(new PartitionKey(SearchParameterStatusWrapper.SearchParameterStatusPartitionKey));

                foreach (SearchParameterStatusWrapper status in statusBatch.Select(x => x.ToSearchParameterStatusWrapper()))
                {
                    status.LastUpdated = Clock.UtcNow;
                    batch.UpsertItem(status);
                }

                await batch.ExecuteAsync(cancellationToken);
            }
        }

        public void SyncStatuses(IReadOnlyCollection<ResourceSearchParameterStatus> statuses)
        {
            // Do nothing. This is only required for SQL.
        }

        public Task<CacheConsistencyResult> CheckCacheConsistencyAsync(DateTime updateEventsSince, DateTime activeHostsSince, CancellationToken cancellationToken)
        {
            throw new NotSupportedException("Cache sync is not supported for Cosmos DB storage.");
        }

        public void Dispose()
        {
            _statusListSemaphore?.Dispose();
        }

        private class CacheQueryResponse
        {
            public int Count { get; set; }

            public DateTimeOffset LastUpdated { get; set; }
        }
    }
}
