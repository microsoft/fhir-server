// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
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
using Microsoft.Health.ControlPlane.CosmosDb.Features.Storage.StoredProcedures.HardDelete;
using Microsoft.Health.CosmosDb.Configs;
using Microsoft.Health.CosmosDb.Features.Queries;
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
        private readonly HardDeleteIdentityProvider _hardDeleteIdentityProvider;
        private readonly RetryExceptionPolicyFactory _retryExceptionPolicyFactory;
        private readonly HardDeleteRole _hardDeleteRole;

        public ControlPlaneDataStore(
            IScoped<IDocumentClient> documentClient,
            CosmosDataStoreConfiguration cosmosDataStoreConfiguration,
            ICosmosDocumentQueryFactory cosmosDocumentQueryFactory,
            RetryExceptionPolicyFactory retryExceptionPolicyFactory,
            IOptionsMonitor<CosmosCollectionConfiguration> namedCosmosCollectionConfigurationAccessor,
            ILogger<ControlPlaneDataStore> logger)
        {
            EnsureArg.IsNotNull(documentClient, nameof(documentClient));
            EnsureArg.IsNotNull(cosmosDataStoreConfiguration, nameof(cosmosDataStoreConfiguration));
            EnsureArg.IsNotNull(cosmosDocumentQueryFactory, nameof(cosmosDocumentQueryFactory));
            EnsureArg.IsNotNull(retryExceptionPolicyFactory, nameof(retryExceptionPolicyFactory));
            EnsureArg.IsNotNull(namedCosmosCollectionConfigurationAccessor, nameof(namedCosmosCollectionConfigurationAccessor));
            EnsureArg.IsNotNull(logger, nameof(logger));

            var collectionConfig = namedCosmosCollectionConfigurationAccessor.Get(Constants.CollectionConfigurationName);

            _documentClient = documentClient;
            _collectionUri = cosmosDataStoreConfiguration.GetRelativeCollectionUri(collectionConfig.CollectionId);
            _retryExceptionPolicyFactory = retryExceptionPolicyFactory;
            _cosmosDocumentQueryFactory = cosmosDocumentQueryFactory;
            _logger = logger;
            _hardDeleteIdentityProvider = new HardDeleteIdentityProvider();
            _hardDeleteRole = new HardDeleteRole();
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
            var cosmosIdentityProviders = await GetSystemDocumentsAsync<CosmosIdentityProvider>(null, CosmosIdentityProvider.IdentityProviderPartition, cancellationToken);
            return cosmosIdentityProviders.Select(cidp => cidp.ToIdentityProvider());
        }

        public async Task<Role> GetRoleAsync(string name, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(name, nameof(name));

            var role = await GetSystemDocumentByIdAsync<CosmosRole>(name, CosmosRole.RolePartition, cancellationToken);
            if (role == null)
            {
                throw new RoleNotFoundException(name);
            }

            return role.ToRole();
        }

        public async Task DeleteRoleAsync(string name, string eTag, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(name, nameof(name));
            await DeleteSystemDocumentsByIdAsync<Role>(name, CosmosRole.RolePartition, eTag, cancellationToken);
        }

        public async Task<IEnumerable<Role>> GetAllRolesAsync(CancellationToken cancellationToken)
        {
            var role = await GetSystemDocumentsAsync<CosmosRole>(null, CosmosRole.RolePartition, cancellationToken);
            return role.Select(cr => cr.ToRole());
        }

        public async Task<UpsertResponse<Role>> UpsertRoleAsync(Role role, string eTag, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(role, nameof(role));

            var cosmosRole = new CosmosRole(role);
            var resultRole = await UpsertSystemObjectAsync(cosmosRole, CosmosRole.RolePartition, eTag, cancellationToken);
            return new UpsertResponse<Role>(resultRole.Resource.ToRole(), resultRole.OutcomeType, resultRole.ETag);
        }

        public async Task<UpsertResponse<IdentityProvider>> UpsertIdentityProviderAsync(IdentityProvider identityProvider, string eTag, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(identityProvider, nameof(identityProvider));

            var cosmosIdentityProvider = new CosmosIdentityProvider(identityProvider);
            var resultIdentityProvider = await UpsertSystemObjectAsync(cosmosIdentityProvider, CosmosIdentityProvider.IdentityProviderPartition, eTag, cancellationToken);
            return new UpsertResponse<IdentityProvider>(resultIdentityProvider.Resource.ToIdentityProvider(), resultIdentityProvider.OutcomeType, resultIdentityProvider.ETag);
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

        internal async Task<IEnumerable<T>> GetSystemDocumentsAsync<T>(List<KeyValuePair<string, object>> filterNameValues, string partitionKey, CancellationToken cancellationToken)
        {
            var documents = await GetDocumentsAsync<Document>(filterNameValues, partitionKey, cancellationToken);
            return documents.Select(r => r.GetPropertyValue<T>("r"));
        }

        internal async Task<IEnumerable<Document>> GetDocumentsAsync<T>(List<KeyValuePair<string, object>> filterNameValues, string partitionKey, CancellationToken cancellationToken, bool appendSystemDataFilter = true)
        {
            EnsureArg.IsNotNull(partitionKey, nameof(partitionKey));

            var queryBuilder = new StringBuilder();
            var queryParameterManager = new QueryParameterManager();

            var queryHelper = new QueryHelper(queryBuilder, queryParameterManager, "r");
            queryHelper.AppendSelectFromRoot("r");

            string filterCondition = "WHERE";

            if (appendSystemDataFilter)
            {
                queryHelper.AppendSystemDataFilter(true);
                filterCondition = "AND";
            }

            if (filterNameValues != null)
            {
                queryHelper.AppendFilterCondition(filterCondition, filterNameValues.Select(kvp => (kvp.Key, kvp.Value)).ToArray());
            }

            var documentQuery = new SqlQuerySpec(
                queryBuilder.ToString(),
                queryParameterManager.ToSqlParameterCollection());

            var feedOptions = new FeedOptions
            {
                PartitionKey = new PartitionKey(partitionKey),
            };

            IDocumentQuery<T> cosmosDocumentQuery =
                CreateDocumentQuery<T>(documentQuery, feedOptions);

            using (cosmosDocumentQuery)
            {
                var retDocuments = new List<Document>();
                while (cosmosDocumentQuery.HasMoreResults)
                {
                    retDocuments.AddRange(await cosmosDocumentQuery.ExecuteNextAsync<Document>(cancellationToken));
                }

                return retDocuments;
            }
        }

        private async Task<T> GetSystemDocumentByIdAsync<T>(string id, string partitionKey, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(id, KnownDocumentProperties.Id);
            var kvps = new List<KeyValuePair<string, object>> { new KeyValuePair<string, object>(KnownDocumentProperties.Id, id) };

            var documents = await GetDocumentsAsync<Document>(kvps, partitionKey, cancellationToken, false);
            var response = documents.Select(r => r.GetPropertyValue<T>("r"));

            var result = response.SingleOrDefault();
            if (result == null)
            {
                _logger.LogError($"{typeof(T)} with id {id} was not found in Cosmos DB.");
            }

            return result;
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

                var outcomeType = response.StatusCode == HttpStatusCode.Created ? UpsertOutcome.Created : UpsertOutcome.Updated;
                return new UpsertResponse<T>((T)(dynamic)response.Resource, outcomeType, response.Resource.ETag);
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
            EnsureArg.IsNotNull(id, KnownDocumentProperties.Id);
            EnsureArg.IsNotNullOrEmpty(partitionKey, nameof(partitionKey));

            try
            {
                StoredProcedureResponse<IList<string>> response;

                string typeName = typeof(T).Name;
                _logger.LogDebug($"Obliterating {id} for type {typeName}");

                switch (typeName)
                {
                    case "IdentityProvider":
                        response = await _retryExceptionPolicyFactory.CreateRetryPolicy().ExecuteAsync(
                                        async ct => await _hardDeleteIdentityProvider.Execute(
                                            _documentClient.Value,
                                            _collectionUri,
                                            id,
                                            eTag,
                                            ct),
                                        cancellationToken);
                        break;
                    case "Role":
                        response = await _retryExceptionPolicyFactory.CreateRetryPolicy().ExecuteAsync(
                                  async ct => await _hardDeleteRole.Execute(
                                   _documentClient.Value,
                                   _collectionUri,
                                   id,
                                   eTag,
                                   ct),
                                  cancellationToken);
                        break;
                    default:
                        throw new InvalidControlPlaneTypeForDeleteException(typeName);
                }

                _logger.LogDebug($"Hard-deleted {response.Response.Count} documents, which consumed {response.RequestCharge} RUs. The list of hard-deleted documents: {string.Join(", ", response.Response)}.");
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
