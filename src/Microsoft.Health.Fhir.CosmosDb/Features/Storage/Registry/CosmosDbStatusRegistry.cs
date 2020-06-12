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
using Microsoft.Extensions.Options;
using Microsoft.Health.CosmosDb.Configs;
using Microsoft.Health.CosmosDb.Features.Storage;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Registry
{
    public class CosmosDbStatusRegistry : ISearchParameterRegistry
    {
        private readonly Func<IScoped<Container>> _documentClientFactory;
        private readonly ICosmosQueryFactory _queryFactory;

        public CosmosDbStatusRegistry(
            Func<IScoped<Container>> documentClientFactory,
            CosmosDataStoreConfiguration cosmosDataStoreConfiguration,
            ICosmosQueryFactory queryFactory)
        {
            EnsureArg.IsNotNull(documentClientFactory, nameof(documentClientFactory));
            EnsureArg.IsNotNull(cosmosDataStoreConfiguration, nameof(cosmosDataStoreConfiguration));
            EnsureArg.IsNotNull(queryFactory, nameof(queryFactory));

            _documentClientFactory = documentClientFactory;
            _queryFactory = queryFactory;
        }

        public Uri CollectionUri { get; set; }

        public async Task<IReadOnlyCollection<ResourceSearchParameterStatus>> GetSearchParameterStatuses()
        {
            using var cancellationSource = new CancellationTokenSource(TimeSpan.FromMinutes(1));
            var parameterStatus = new List<ResourceSearchParameterStatus>();
            using IScoped<Container> clientScope = _documentClientFactory.Invoke();

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

        public async Task UpdateStatuses(IEnumerable<ResourceSearchParameterStatus> statuses)
        {
            EnsureArg.IsNotNull(statuses, nameof(statuses));

            using var clientScope = _documentClientFactory.Invoke();
            var batch = clientScope.Value.CreateTransactionalBatch(new PartitionKey(SearchParameterStatusWrapper.SearchParameterStatusPartitionKey));

            foreach (var status in statuses.Select(x => x.ToSearchParameterStatusWrapper()))
            {
                batch.UpsertItem(status);
            }

            await batch.ExecuteAsync();
        }
    }
}
