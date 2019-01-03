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
using Microsoft.Azure.Documents.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.ControlPlane.Core.Features.Exceptions;
using Microsoft.Health.ControlPlane.Core.Features.Persistence;
using Microsoft.Health.ControlPlane.Core.Features.Rbac;
using Microsoft.Health.ControlPlane.Core.Features.Rbac.Roles;
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
        private readonly string _collectionId;
        private readonly string _databaseId;

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
            _collectionId = cosmosDataStoreConfiguration.ControlPlaneCollectionId;
            _databaseId = cosmosDataStoreConfiguration.DatabaseId;
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

        public async Task<string> DeleteRoleAsync(string name)
        {
            EnsureArg.IsNotNull(name, nameof(name));

            var response = await DeleteSystemDocumentByIdAsync<CosmosRole>(name, CosmosRole.RolePartition);

            if (response == null)
            {
                throw new RoleNotFoundException(name);
            }

            return response;
        }

        public async Task<IEnumerable<Role>> GetRoleAllAsync(CancellationToken cancellationToken)
        {
            var role = await GetSystemDocumentAllAsync<CosmosRole>(cancellationToken);

            if (role == null)
            {
                throw new RoleNotFoundException("all");
            }

            return role.ToRoleList();
        }

        public async Task<Role> UpsertRoleAsync(Role role, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(role, nameof(role));

            var cosmosRole = new CosmosRole(role);
            var resultRole = await UpsertSystemObjectAsync(cosmosRole, CosmosRole.RolePartition, cancellationToken);
            return resultRole.ToRole();
        }

        public async Task<Role> AddRoleAsync(Role role, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(role, nameof(role));

            var cosmosRole = new CosmosRole(role);
            var resultRole = await AddSystemObjectAsync(cosmosRole, CosmosRole.RolePartition, cancellationToken);
            return resultRole.ToRole();
        }

        public async Task<IdentityProvider> UpsertIdentityProviderAsync(IdentityProvider identityProvider, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(identityProvider, nameof(identityProvider));

            var cosmosIdentityProvider = new CosmosIdentityProvider(identityProvider);
            var resultIdentityProvider = await UpsertSystemObjectAsync(cosmosIdentityProvider, CosmosIdentityProvider.IdentityProviderPartition, cancellationToken);
            return resultIdentityProvider.ToIdentityProvider();
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

        private async Task<IEnumerable<T>> GetSystemDocumentAllAsync<T>(CancellationToken cancellationToken)
        {
            var documentQuery = new SqlQuerySpec(
                "SELECT * FROM root");

            var feedOptions = new FeedOptions
            {
                EnableCrossPartitionQuery = true,
                MaxItemCount = 10,
            };

            IDocumentQuery<T> cosmosDocumentQuery =
                CreateDocumentQuery<T>(documentQuery, feedOptions);
            using (cosmosDocumentQuery)
            {
                var response = await cosmosDocumentQuery.ExecuteNextAsync(cancellationToken);
                var result = (dynamic)response;
                if (result == null)
                {
                    _logger.LogError("Results were not found in Cosmos DB.");
                }

                return result;
            }
        }

        private async Task<T> UpsertSystemObjectAsync<T>(T systemObject, string partitionKey, CancellationToken cancellationToken)
            where T : class
        {
            EnsureArg.IsNotNull(systemObject, nameof(systemObject));
            var eTagAccessCondition = new AccessCondition();

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

        private async Task<T> AddSystemObjectAsync<T>(T systemObject, string partitionKey, CancellationToken cancellationToken)
           where T : class
        {
            EnsureArg.IsNotNull(systemObject, nameof(systemObject));
            var eTagAccessCondition = new AccessCondition();

            var requestOptions = new RequestOptions
            {
                PartitionKey = new PartitionKey(partitionKey),
                AccessCondition = eTagAccessCondition,
            };
            try
            {
                var response = await _documentClient.Value.CreateDocumentAsync(
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

        private async Task<string> DeleteSystemDocumentByIdAsync<T>(string id, string partitionKey)
        {
            EnsureArg.IsNotNull(id, nameof(id));
            EnsureArg.IsNotNull(partitionKey, nameof(partitionKey));
            var eTagAccessCondition = new AccessCondition();

            try
            {
                var documentUri = UriFactory.CreateDocumentUri(_databaseId, _collectionId, id);

                var requestOptions = new RequestOptions
                {
                    PartitionKey = new PartitionKey(partitionKey),
                    AccessCondition = eTagAccessCondition,
                };

                var response = await _documentClient.Value.DeleteDocumentAsync(documentUri.ToString(), requestOptions);

                return "success";
            }
            catch (DocumentClientException dce)
            {
                _logger.LogError(dce, "Unhandled Document Client Exception");
                throw;
            }
        }
    }
}
