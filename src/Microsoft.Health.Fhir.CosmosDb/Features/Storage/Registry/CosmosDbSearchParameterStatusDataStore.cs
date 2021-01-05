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
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.CosmosDb.Configs;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Registry
{
    public class CosmosDbSearchParameterStatusDataStore : ISearchParameterStatusDataStore
    {
        private readonly Func<IScoped<Container>> _containerScopeFactory;
        private readonly ICosmosQueryFactory _queryFactory;

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
            var parameterStatus = new List<ResourceSearchParameterStatus>();
            using IScoped<Container> clientScope = _containerScopeFactory.Invoke();

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

            return parameterStatus;
        }

        public async Task UpsertStatuses(List<ResourceSearchParameterStatus> statuses)
        {
            EnsureArg.IsNotNull(statuses, nameof(statuses));

            if (statuses.Count == 0)
            {
                return;
            }

            using var clientScope = _containerScopeFactory.Invoke();
            var batch = clientScope.Value.CreateTransactionalBatch(new PartitionKey(SearchParameterStatusWrapper.SearchParameterStatusPartitionKey));

            foreach (var status in statuses.Select(x => x.ToSearchParameterStatusWrapper()))
            {
                batch.UpsertItem(status);
            }

            await batch.ExecuteAsync();
        }
    }
}
