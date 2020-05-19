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
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Extensions.Options;
using Microsoft.Health.CosmosDb.Configs;
using Microsoft.Health.CosmosDb.Features.Storage;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Registry
{
    public class CosmosDbStatusRegistryDataStore : ISearchParameterRegistryDataStore
    {
        private readonly Func<IScoped<IDocumentClient>> _documentClientFactory;
        private readonly ICosmosDocumentQueryFactory _queryFactory;

        public CosmosDbStatusRegistryDataStore(
            Func<IScoped<IDocumentClient>> documentClientFactory,
            CosmosDataStoreConfiguration cosmosDataStoreConfiguration,
            ICosmosDocumentQueryFactory queryFactory,
            IOptionsMonitor<CosmosCollectionConfiguration> namedCosmosCollectionConfigurationAccessor)
        {
            EnsureArg.IsNotNull(documentClientFactory, nameof(documentClientFactory));
            EnsureArg.IsNotNull(cosmosDataStoreConfiguration, nameof(cosmosDataStoreConfiguration));
            EnsureArg.IsNotNull(queryFactory, nameof(queryFactory));
            EnsureArg.IsNotNull(namedCosmosCollectionConfigurationAccessor, nameof(namedCosmosCollectionConfigurationAccessor));

            _documentClientFactory = documentClientFactory;
            _queryFactory = queryFactory;

            CosmosCollectionConfiguration collectionConfiguration = namedCosmosCollectionConfigurationAccessor.Get(Constants.CollectionConfigurationName);
            CollectionUri = cosmosDataStoreConfiguration.GetRelativeCollectionUri(collectionConfiguration.CollectionId);
        }

        public Uri CollectionUri { get; set; }

        public async Task<IReadOnlyCollection<ResourceSearchParameterStatus>> GetSearchParameterStatuses(CancellationToken cancellationToken)
        {
            var parameterStatus = new List<ResourceSearchParameterStatus>();
            using var clientScope = _documentClientFactory.Invoke();

            var query = _queryFactory.Create<SearchParameterStatusWrapper>(
                    clientScope.Value,
                    new CosmosQueryContext(
                        CollectionUri,
                        new SqlQuerySpec("select * from c"),
                        new FeedOptions
                        {
                            PartitionKey =
                                new PartitionKey(SearchParameterStatusWrapper.SearchParameterStatusPartitionKey),
                        }));

            do
            {
                FeedResponse<SearchParameterStatusWrapper> results = await query.ExecuteNextAsync<SearchParameterStatusWrapper>(cancellationToken);
                parameterStatus.AddRange(results
                    .Select(x => x.ToSearchParameterStatus()));
            }
            while (query.HasMoreResults);

            return parameterStatus;
        }

        public async Task UpsertStatuses(IEnumerable<ResourceSearchParameterStatus> statuses, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(statuses, nameof(statuses));

            using var clientScope = _documentClientFactory.Invoke();

            foreach (var status in statuses.Select(x => x.ToSearchParameterStatusWrapper()))
            {
                await clientScope.Value.UpsertDocumentAsync(CollectionUri, status, null, false, cancellationToken);
            }
        }
    }
}
