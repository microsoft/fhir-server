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
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.CosmosDb.Configs;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Registry
{
    public sealed class CosmosDbSearchParameterStatusDataStore : ISearchParameterStatusDataStore, IDisposable
    {
        private readonly Func<IScoped<Container>> _containerScopeFactory;
        private readonly ICosmosQueryFactory _queryFactory;
        private DateTimeOffset? _lastRefreshed = null;
        private List<ResourceSearchParameterStatus> _statusList = new();
        private readonly SemaphoreSlim _statusListSemaphore = new(1, 1);

        public CosmosDbSearchParameterStatusDataStore(
            Func<IScoped<Container>> containerScopeFactory,
            CosmosDataStoreConfiguration cosmosDataStoreConfiguration,
            ICosmosQueryFactory queryFactory)
        {
            EnsureArg.IsNotNull(containerScopeFactory, nameof(containerScopeFactory));
            EnsureArg.IsNotNull(cosmosDataStoreConfiguration, nameof(cosmosDataStoreConfiguration));
            EnsureArg.IsNotNull(queryFactory, nameof(queryFactory));

            _containerScopeFactory = containerScopeFactory;
            _queryFactory = queryFactory;
        }

        public async Task<IReadOnlyCollection<ResourceSearchParameterStatus>> GetSearchParameterStatuses()
        {
            using var cancellationSource = new CancellationTokenSource(TimeSpan.FromMinutes(1));
            using IScoped<Container> clientScope = _containerScopeFactory.Invoke();
            DateTimeOffset startedCheck = Clock.UtcNow;

            await _statusListSemaphore.WaitAsync(cancellationSource.Token);
            try
            {
                if (_lastRefreshed.HasValue)
                {
                    bool updateRequired = await CheckIfSearchParameterStatusUpdateRequiredAsync(
                        clientScope,
                        _statusList.Count,
                        _lastRefreshed.Value,
                        cancellationSource.Token);

                    if (!updateRequired)
                    {
                        return _statusList;
                    }
                }

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
                        FeedResponse<SearchParameterStatusWrapper> results = await query.ExecuteNextAsync();

                        parameterStatus.AddRange(results.Select(x => x.ToSearchParameterStatus()));
                    }
                    while (query.HasMoreResults);

                    if (!parameterStatus.Any())
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), cancellationSource.Token);
                    }
                }
                while (!parameterStatus.Any() && !cancellationSource.IsCancellationRequested);

                _lastRefreshed = startedCheck;
                _statusList = parameterStatus;

                return _statusList;
            }
            finally
            {
                _statusListSemaphore.Release();
            }
        }

        private async Task<bool> CheckIfSearchParameterStatusUpdateRequiredAsync(IScoped<Container> container, int currentCount, DateTimeOffset lastRefreshed, CancellationToken cancellationToken)
        {
            var countQuery = _queryFactory.Create<int>(
                container.Value,
                new CosmosQueryContext(
                    new QueryDefinition("select value count(0) from c"),
                    new QueryRequestOptions
                    {
                        PartitionKey = new PartitionKey(SearchParameterStatusWrapper.SearchParameterStatusPartitionKey),
                        MaxItemCount = 1,
                    }));

            FeedResponse<int> countResponse = await countQuery.ExecuteNextAsync(cancellationToken);
            if (countResponse.FirstOrDefault() != currentCount)
            {
                return true;
            }

            var lastUpdatedQuery = _queryFactory.Create<DateTimeOffset>(
                container.Value,
                new CosmosQueryContext(
                    new QueryDefinition("select value c.lastUpdated from c order by c.lastUpdated desc"),
                    new QueryRequestOptions
                    {
                        PartitionKey = new PartitionKey(SearchParameterStatusWrapper.SearchParameterStatusPartitionKey),
                        MaxItemCount = 1,
                    }));

            FeedResponse<DateTimeOffset> lastUpdatedResponse = await lastUpdatedQuery.ExecuteNextAsync(cancellationToken);
            if (lastUpdatedResponse.FirstOrDefault() > lastRefreshed)
            {
                return true;
            }

            return false;
        }

        public async Task UpsertStatuses(IReadOnlyCollection<ResourceSearchParameterStatus> statuses)
        {
            EnsureArg.IsNotNull(statuses, nameof(statuses));

            if (statuses.Count == 0)
            {
                return;
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

                await batch.ExecuteAsync();
            }
        }

        public void Dispose()
        {
            _statusListSemaphore?.Dispose();
        }
    }
}
