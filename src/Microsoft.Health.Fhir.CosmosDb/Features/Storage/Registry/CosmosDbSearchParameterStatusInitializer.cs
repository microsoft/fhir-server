﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.CosmosDb.Configs;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Versioning;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Registry
{
    public class CosmosDbSearchParameterStatusInitializer : ICollectionUpdater
    {
        private readonly ISearchParameterStatusDataStore _filebasedSearchParameterStatusDataStore;
        private readonly ICosmosQueryFactory _queryFactory;
        private readonly CosmosDataStoreConfiguration _configuration;

        public CosmosDbSearchParameterStatusInitializer(
            FilebasedSearchParameterStatusDataStore.Resolver filebasedSearchParameterStatusDataStoreResolver,
            ICosmosQueryFactory queryFactory,
            CosmosDataStoreConfiguration configuration)
        {
            EnsureArg.IsNotNull(filebasedSearchParameterStatusDataStoreResolver, nameof(filebasedSearchParameterStatusDataStoreResolver));
            EnsureArg.IsNotNull(queryFactory, nameof(queryFactory));

            _filebasedSearchParameterStatusDataStore = filebasedSearchParameterStatusDataStoreResolver.Invoke();
            _queryFactory = queryFactory;
            _configuration = configuration;
        }

        public async Task ExecuteAsync(Container container, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(container, nameof(container));

            // Detect if registry has been initialized
            var partitionKey = new PartitionKey(SearchParameterStatusWrapper.SearchParameterStatusPartitionKey);
            var query = _queryFactory.Create<dynamic>(
                container,
                new CosmosQueryContext(
                    new QueryDefinition($"SELECT TOP 1 * FROM c where c.{KnownDocumentProperties.PartitionKey} = '{SearchParameterStatusWrapper.SearchParameterStatusPartitionKey}'"),
                    new QueryRequestOptions { PartitionKey = partitionKey }));

            var results = await query.ExecuteNextAsync(cancellationToken);

            if (!results.Any())
            {
                var statuses = await _filebasedSearchParameterStatusDataStore.GetSearchParameterStatuses(cancellationToken);

                foreach (var status in statuses.Where(x => _configuration.InitialSortParameterUris.Contains(x.Uri.OriginalString)))
                {
                    status.SortStatus = SortParameterStatus.Enabled;
                }

                foreach (var batch in statuses.TakeBatch(100))
                {
                    TransactionalBatch transaction = container.CreateTransactionalBatch(partitionKey);

                    foreach (SearchParameterStatusWrapper status in batch.Select(x => x.ToSearchParameterStatusWrapper()))
                    {
                        transaction.CreateItem(status);
                    }

                    await transaction.ExecuteAsync(cancellationToken);
                }
            }
        }
    }
}
