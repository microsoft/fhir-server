// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.CosmosDb.Core.Configs;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Initialization.Features.Storage.StoredProcedures.UpdateUnsupportedSearchParametersToUnsupported;
using static System.Net.WebRequestMethods;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Registry
{
    public class CosmosDbSearchParameterStatusInitializer : ICollectionDataUpdater
    {
        private readonly ISearchParameterStatusDataStore _filebasedSearchParameterStatusDataStore;
        private readonly ICosmosQueryFactory _queryFactory;
        private readonly CosmosDataStoreConfiguration _configuration;
        private readonly UpdateUnsupportedSearchParameters _updateSP = new();

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

            var statuses = await _filebasedSearchParameterStatusDataStore.GetSearchParameterStatuses(cancellationToken);
            var uris = await GetAllSearchParameterUrls(container, cancellationToken);
            var unloadedStatuses = statuses.Where(x => !uris.Contains(x.Uri.OriginalString)).ToList();

            if (unloadedStatuses.Count != 0)
            {
                if (await IsDataPresent(container, cancellationToken))
                {
                    foreach (var status in unloadedStatuses.Where(x => x.Uri.OriginalString != "http://hl7.org/fhir/SearchParameter/Resource-type"))
                    {
                        status.Status = SearchParameterStatus.Supported;
                    }
                }

                foreach (var status in unloadedStatuses.Where(x => _configuration.InitialSortParameterUris.Contains(x.Uri.OriginalString)))
                {
                    status.SortStatus = SortParameterStatus.Enabled;
                }

                var partitionKey = new PartitionKey(SearchParameterStatusWrapper.SearchParameterStatusPartitionKey);
                foreach (var batch in unloadedStatuses.TakeBatch(100))
                {
                    TransactionalBatch transaction = container.CreateTransactionalBatch(partitionKey);

                    foreach (SearchParameterStatusWrapper status in batch.Select(x => x.ToSearchParameterStatusWrapper()))
                    {
                        transaction.CreateItem(status);
                    }

                    await transaction.ExecuteAsync(cancellationToken);
                }
            }
            else
            {
                await _updateSP.Execute(container.Scripts, cancellationToken);
            }
        }

        public async Task<List<string>> GetAllSearchParameterUrls(Container container, CancellationToken cancellationToken)
        {
            var uris = new List<string>();

            var partitionKey = new PartitionKey(SearchParameterStatusWrapper.SearchParameterStatusPartitionKey);
            var query = _queryFactory.Create<dynamic>(
                container: container,
                new CosmosQueryContext(
                    new QueryDefinition($"SELECT VALUE c.uri FROM c where c.{KnownDocumentProperties.PartitionKey} = '{SearchParameterStatusWrapper.SearchParameterStatusPartitionKey}'"),
                    new QueryRequestOptions { PartitionKey = partitionKey }));

            var results = await query.ExecuteNextAsync(cancellationToken);
            uris.AddRange(results.Select(x => (string)x));

            while (results.ContinuationToken != null)
            {
                results = await query.ExecuteNextAsync(cancellationToken);
                uris.AddRange(results.Select(x => (string)x));
            }

            return uris;
        }

        public async Task<bool> IsDataPresent(Container container, CancellationToken cancellationToken)
        {
            var query = _queryFactory.Create<dynamic>(
                container: container,
                new CosmosQueryContext(new QueryDefinition($"SELECT TOP 1 c.id FROM c where c.resourceId != null")));
            var results = await query.ExecuteNextAsync(cancellationToken);
            return results.Any();
        }
    }
}
