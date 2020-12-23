// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Versioning;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Registry
{
    public class CosmosDbSearchParameterStatusInitializer : ICollectionUpdater
    {
        private readonly ISearchParameterStatusDataStore _filebasedSearchParameterStatusDataStore;
        private readonly ICosmosQueryFactory _queryFactory;

        public CosmosDbSearchParameterStatusInitializer(
            FilebasedSearchParameterStatusDataStore.Resolver filebasedSearchParameterStatusDataStoreResolver,
            ICosmosQueryFactory queryFactory)
        {
            EnsureArg.IsNotNull(filebasedSearchParameterStatusDataStoreResolver, nameof(filebasedSearchParameterStatusDataStoreResolver));
            EnsureArg.IsNotNull(queryFactory, nameof(queryFactory));

            _filebasedSearchParameterStatusDataStore = filebasedSearchParameterStatusDataStoreResolver.Invoke();
            _queryFactory = queryFactory;
        }

        public async Task ExecuteAsync(Container collection)
        {
            EnsureArg.IsNotNull(collection, nameof(collection));

            // Detect if registry has been initialized
            var partitionKey = new PartitionKey(SearchParameterStatusWrapper.SearchParameterStatusPartitionKey);
            var query = _queryFactory.Create<dynamic>(
                collection,
                new CosmosQueryContext(
                    new QueryDefinition($"SELECT TOP 1 * FROM c where c.{KnownDocumentProperties.PartitionKey} = '{SearchParameterStatusWrapper.SearchParameterStatusPartitionKey}'"),
                    new QueryRequestOptions { PartitionKey = partitionKey }));

            var results = await query.ExecuteNextAsync();

            if (!results.Any())
            {
                var statuses = await _filebasedSearchParameterStatusDataStore.GetSearchParameterStatuses();

                foreach (var batch in statuses.TakeBatch(100))
                {
                    TransactionalBatch transaction = collection.CreateTransactionalBatch(partitionKey);

                    foreach (SearchParameterStatusWrapper status in batch.Select(x => x.ToSearchParameterStatusWrapper()))
                    {
                        transaction.CreateItem(status);
                    }

                    await transaction.ExecuteAsync();
                }
            }
        }
    }
}
