// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.ControlPlane.Core.Features.Exceptions;
using Microsoft.Health.ControlPlane.Core.Features.Persistence;
using Microsoft.Health.ControlPlane.Core.Features.Rbac;
using Microsoft.Health.ControlPlane.CosmosDb.Features.Storage.Rbac;
using Microsoft.Health.CosmosDb.Configs;
using Microsoft.Health.CosmosDb.Features.Storage;
using Microsoft.Health.Extensions.DependencyInjection;

namespace Microsoft.Health.ControlPlane.CosmosDb.Features.Storage
{
    public class ControlPlaneDataStore : IControlPlaneDataStore
    {
        private readonly IScoped<IDocumentClient> _documentClient;
        private readonly ICosmosDocumentQueryFactory _cosmosDocumentQueryFactory;
        private readonly ILogger<ControlPlaneDataStore> _logger;
        private readonly Uri _collectionUri;

        public ControlPlaneDataStore(
            IScoped<IDocumentClient> documentClient,
            CosmosDataStoreConfiguration cosmosDataStoreConfiguration,
            ICosmosDocumentQueryFactory cosmosDocumentQueryFactory,
            IOptionsMonitor<CosmosCollectionConfiguration> namedCosmosCollectionConfigurationAccessor,
            ILogger<ControlPlaneDataStore> logger)
        {
            EnsureArg.IsNotNull(documentClient, nameof(documentClient));
            EnsureArg.IsNotNull(cosmosDataStoreConfiguration, nameof(cosmosDataStoreConfiguration));
            EnsureArg.IsNotNull(cosmosDocumentQueryFactory, nameof(cosmosDocumentQueryFactory));
            EnsureArg.IsNotNull(namedCosmosCollectionConfigurationAccessor, nameof(namedCosmosCollectionConfigurationAccessor));
            EnsureArg.IsNotNull(logger, nameof(logger));

            var collectionConfig = namedCosmosCollectionConfigurationAccessor.Get(Constants.CollectionConfigurationName);

            _documentClient = documentClient;
            _collectionUri = cosmosDataStoreConfiguration.GetRelativeCollectionUri(collectionConfig.CollectionId);
            _cosmosDocumentQueryFactory = cosmosDocumentQueryFactory;
            _logger = logger;
        }

        public async Task<IdentityProvider> GetIdentityProviderAsync(string name, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(name, nameof(name));

            var identityProvider = await GetSystemDocumentByIdAsync<CosmosIdentityProvider>(name, CosmosIdentityProvider.IdentityProviderPartition, cancellationToken);
            if (identityProvider == null)
            {
                throw new IdentityProviderNotFoundException(name);
            }

            return identityProvider.ToIdentityProvider();
        }

        public async Task<IEnumerable<IdentityProvider>> GetAllIdentityProvidersAsync(CancellationToken cancellationToken)
        {
            return await GetSystemDocumentsByIdAsync<CosmosIdentityProvider>(null, CosmosIdentityProvider.IdentityProviderPartition, cancellationToken);
        }

        public async Task<UpsertResponse<IdentityProvider>> UpsertIdentityProviderAsync(IdentityProvider identityProvider, string versionIdETag, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(identityProvider, nameof(identityProvider));

            var cosmosIdentityProvider = new CosmosIdentityProvider(identityProvider);
            var resultIdentityProvider = await UpsertSystemObjectAsync(cosmosIdentityProvider, CosmosIdentityProvider.IdentityProviderPartition, versionIdETag, cancellationToken);
            return new UpsertResponse<IdentityProvider>(resultIdentityProvider.ControlPlaneResource.ToIdentityProvider(), resultIdentityProvider.OutcomeType, resultIdentityProvider.ETag);
        }

        public async Task DeleteIdentityProviderAsync(string name, string eTag, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(name, nameof(name));
            await DeleteSystemDocumentsByIdAsync<IdentityProvider>(name, CosmosIdentityProvider.IdentityProviderPartition, eTag, cancellationToken);
        }

        internal IDocumentQuery<T> CreateDocumentQuery<T>(
            SqlQuerySpec sqlQuerySpec,
            FeedOptions feedOptions = null)
        {
            EnsureArg.IsNotNull(sqlQuerySpec, nameof(sqlQuerySpec));

            var context = new CosmosQueryContext(_collectionUri, sqlQuerySpec, feedOptions);

            return _cosmosDocumentQueryFactory.Create<T>(_documentClient.Value, context);
        }

        private async Task<T> GetSystemDocumentByIdAsync<T>(string id, string partitionKey, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(id, nameof(id));
            var response = await GetSystemDocumentsByIdAsync<T>(id, partitionKey, cancellationToken);
            var result = response.SingleOrDefault();
            if (result == null)
            {
                _logger.LogError($"{typeof(T)} with id {id} was not found in Cosmos DB.");
            }

            return result;
        }

        private async Task<IEnumerable<T>> GetSystemDocumentsByIdAsync<T>(string id, string partitionKey, CancellationToken cancellationToken)
        {
            FeedResponse<Document> response = await GetSystemDocuments<T>(id, partitionKey, cancellationToken);
            return response.Select(r => r.GetPropertyValue<T>("r"));
        }

        private async Task<FeedResponse<Document>> GetSystemDocuments<T>(string id, string partitionKey, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(partitionKey, nameof(partitionKey));

            var documentQuery = string.IsNullOrWhiteSpace(id)
                ? new SqlQuerySpec("SELECT r, r._self FROM root r")
                : new SqlQuerySpec(
                "SELECT r, r._self FROM root r WHERE r.id = @id",
                new SqlParameterCollection(new[] { new SqlParameter("@id", id) }));

            var feedOptions = new FeedOptions
            {
                PartitionKey = new PartitionKey(partitionKey),
            };

            IDocumentQuery<T> cosmosDocumentQuery =
                CreateDocumentQuery<T>(documentQuery, feedOptions);

            using (cosmosDocumentQuery)
            {
                FeedResponse<Document> response = await cosmosDocumentQuery.ExecuteNextAsync<Document>(cancellationToken);
                return response;
            }
        }

        private async Task<UpsertResponse<T>> UpsertSystemObjectAsync<T>(T systemObject, string partitionKey, string eTag,  CancellationToken cancellationToken)
            where T : class
        {
            EnsureArg.IsNotNull(systemObject, nameof(systemObject));
            RequestOptions requestOptions = GetRequestOptions(partitionKey, eTag);

            try
            {
                var response = await _documentClient.Value.UpsertDocumentAsync(
                    _collectionUri,
                    systemObject,
                    requestOptions,
                    true,
                    cancellationToken);
                _logger.LogInformation("Request charge: {RequestCharge}, latency: {RequestLatency}", response.RequestCharge, response.RequestLatency);

                var outcomeType = response.StatusCode == HttpStatusCode.Created ? UpsertOutcomeType.Created : UpsertOutcomeType.Updated;
                return new UpsertResponse<T>((T)(dynamic)response.Resource, outcomeType, response.Resource.ETag);
            }
            catch (DocumentClientException dce)
            {
                _logger.LogError(dce, "Unhandled Document Client Exception");
                throw;
            }
        }

        private static RequestOptions GetRequestOptions(string partitionKey, string eTag)
        {
            var eTagAccessCondition = new AccessCondition();

            if (!string.IsNullOrWhiteSpace(eTag))
            {
                eTagAccessCondition.Type = AccessConditionType.IfMatch;
                eTagAccessCondition.Condition = eTag;
            }

            var requestOptions = new RequestOptions
            {
                PartitionKey = new PartitionKey(partitionKey),
                AccessCondition = eTagAccessCondition,
            };
            return requestOptions;
        }

        private async Task DeleteSystemDocumentsByIdAsync<T>(string id, string partitionKey, string eTag, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(id, nameof(id));
            FeedResponse<Document> response = await GetSystemDocuments<T>(id, partitionKey, cancellationToken);
            var document = response.SingleOrDefault();

            if (document == null)
            {
                throw new IdentityProviderNotFoundException(id);
            }

            RequestOptions requestOptions = GetRequestOptions(partitionKey, eTag);

            try
            {
                var deleteResponse = await _documentClient.Value.DeleteDocumentAsync(
                    document.SelfLink,
                    requestOptions,
                    cancellationToken);
                _logger.LogInformation("Request charge: {RequestCharge}, latency: {RequestLatency}", deleteResponse.RequestCharge, deleteResponse.RequestLatency);
            }
            catch (DocumentClientException dce)
            {
                if (dce.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    throw new RequestRateExceededException(dce.RetryAfter);
                }

                _logger.LogError(dce, "Unhandled Document Client Exception");
                throw;
            }
        }
    }
}
