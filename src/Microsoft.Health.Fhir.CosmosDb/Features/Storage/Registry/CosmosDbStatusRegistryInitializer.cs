// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Health.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Versioning;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Registry
{
    public class CosmosDbStatusRegistryInitializer : IFhirCollectionUpdater
    {
        private readonly ISearchParameterRegistryDataStore _filebasedRegistry;
        private readonly ICosmosDocumentQueryFactory _queryFactory;

        public CosmosDbStatusRegistryInitializer(
            FilebasedSearchParameterRegistryDataStore.Resolver filebasedRegistry,
            ICosmosDocumentQueryFactory queryFactory)
        {
            EnsureArg.IsNotNull(filebasedRegistry, nameof(filebasedRegistry));
            EnsureArg.IsNotNull(queryFactory, nameof(queryFactory));

            _filebasedRegistry = filebasedRegistry.Invoke();
            _queryFactory = queryFactory;
        }

        public async Task ExecuteAsync(IDocumentClient client, DocumentCollection collection, Uri relativeCollectionUri)
        {
            EnsureArg.IsNotNull(client, nameof(client));
            EnsureArg.IsNotNull(collection, nameof(collection));
            EnsureArg.IsNotNull(relativeCollectionUri, nameof(relativeCollectionUri));

            // Detect if registry has been initialized
            var query = _queryFactory.Create<dynamic>(
                client,
                new CosmosQueryContext(
                    relativeCollectionUri,
                    new SqlQuerySpec($"SELECT TOP 1 * FROM c where c.{KnownDocumentProperties.PartitionKey} = '{SearchParameterStatusWrapper.SearchParameterStatusPartitionKey}'")));

            var results = await query.ExecuteNextAsync();

            if (!results.Any())
            {
                var statuses = await _filebasedRegistry.GetSearchParameterStatuses();

                foreach (SearchParameterStatusWrapper status in statuses.Select(x => x.ToSearchParameterStatusWrapper()))
                {
                    await client.UpsertDocumentAsync(relativeCollectionUri, status);
                }
            }
        }
    }
}
