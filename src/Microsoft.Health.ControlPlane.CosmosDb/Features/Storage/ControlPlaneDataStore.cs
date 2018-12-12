// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Health.ControlPlane.Core.Features.Persistence;
using Microsoft.Health.ControlPlane.Core.Features.Rbac;
using Microsoft.Health.CosmosDb.Configs;
using Microsoft.Health.CosmosDb.Features.Storage;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.ControlPlane;

namespace Microsoft.Health.ControlPlane.CosmosDb.Features.Storage
{
    public class ControlPlaneDataStore : IControlPlaneDataStore
    {
        private readonly IScoped<IDocumentClient> _documentClient;
        private readonly CosmosDataStoreConfiguration _cosmosDataStoreConfiguration;
        private readonly ICosmosDocumentQueryFactory _cosmosDocumentQueryFactory;
        private readonly ILogger<ControlPlaneDataStore> _logger;
        private readonly Uri _collectionUri;

        public ControlPlaneDataStore(
            IScoped<IDocumentClient> documentClient,
            CosmosDataStoreConfiguration cosmosDataStoreConfiguration,
            ICosmosDocumentQueryFactory cosmosDocumentQueryFactory,
            ILogger<ControlPlaneDataStore> logger)
        {
            EnsureArg.IsNotNull(documentClient, nameof(documentClient));
            EnsureArg.IsNotNull(cosmosDataStoreConfiguration, nameof(cosmosDataStoreConfiguration));
            EnsureArg.IsNotNull(cosmosDocumentQueryFactory, nameof(cosmosDocumentQueryFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _documentClient = documentClient;
            _cosmosDataStoreConfiguration = cosmosDataStoreConfiguration;
            _collectionUri = _cosmosDataStoreConfiguration.RelativeControlPlaneCollectionUri;
            _cosmosDocumentQueryFactory = cosmosDocumentQueryFactory;
            _logger = logger;
        }

        public async Task<IdentityProvider> GetIdentityProviderAsync(string name, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(name, nameof(name));

            var identityProvider = await GetSystemDocumentByIdAsync<CosmosIdentityProvider>(name, CosmosIdentityProvider.IdentityProviderPartition, cancellationToken);
            if (identityProvider == null)
            {
                throw new Exception("Invalid role name");
            }

            return identityProvider.ToIdentityProvider();
        }

        public async Task<IdentityProvider> UpsertIdentityProviderAsync(IdentityProvider identityProvider, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(identityProvider, nameof(identityProvider));

            var cosmosIdentityProvider = new CosmosIdentityProvider(identityProvider);
            var resultIdentityProvider = await UpsertSystemObjectAsync(cosmosIdentityProvider, CosmosIdentityProvider.IdentityProviderPartition, cancellationToken);
            return resultIdentityProvider.ToIdentityProvider();
        }

        // TODO: Remove duplication between here and cosmosdatastore
        internal IDocumentQuery<T> CreateDocumentQuery<T>(
            SqlQuerySpec sqlQuerySpec,
            FeedOptions feedOptions = null)
        {
            EnsureArg.IsNotNull(sqlQuerySpec, nameof(sqlQuerySpec));

            CosmosQueryContext context = new CosmosQueryContext(_collectionUri, sqlQuerySpec, feedOptions);

            // TODO: Remove Fhir specifics here, or overload for non fhir
            return _cosmosDocumentQueryFactory.Create<T>(_documentClient.Value, context);
        }

        private async Task<T> GetSystemDocumentByIdAsync<T>(string id, string partitionKey, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(id, nameof(id));
            EnsureArg.IsNotNull(partitionKey, nameof(partitionKey));
            var documentQuery = new SqlQuerySpec(
                "SELECT * FROM root r WHERE r.id = @id",
                new SqlParameterCollection(new[] { new SqlParameter("@id", id) }));
            var feedOptions = new FeedOptions
            {
                PartitionKey = new PartitionKey(partitionKey),
            };
            IDocumentQuery<T> cosmosDocumentQuery =
                CreateDocumentQuery<T>(documentQuery, feedOptions);
            using (cosmosDocumentQuery)
            {
                var response = await cosmosDocumentQuery.ExecuteNextAsync(cancellationToken);
                var result = response.SingleOrDefault();
                if (result == null)
                {
                    _logger.LogError($"{typeof(T)} with id {id} was not found in Cosmos DB.");
                }

                return result;
            }
        }

        private async Task<T> UpsertSystemObjectAsync<T>(T systemObject, string partitionKey, CancellationToken cancellationToken)
            where T : class
        {
            EnsureArg.IsNotNull(systemObject, nameof(systemObject));
            var eTagAccessCondition = new AccessCondition();

            // TODO: abstract back in BeginTimedScope
            var systemStopwatch = new Stopwatch();
            systemStopwatch.Start();
            var requestOptions = new RequestOptions
            {
                PartitionKey = new PartitionKey(partitionKey),
                AccessCondition = eTagAccessCondition,
            };
            try
            {
                var response = await _documentClient.Value.UpsertDocumentAsync(
                    _collectionUri,
                    systemObject,
                    requestOptions,
                    true,
                    cancellationToken);
                _logger.LogInformation("Request charge: {RequestCharge}, latency: {RequestLatency}", response.RequestCharge, response.RequestLatency);
                return (dynamic)response.Resource;
            }
            catch (DocumentClientException dce)
            {
                _logger.LogError(dce, "Unhandled Document Client Exception");
                throw;
            }
        }
    }
}
