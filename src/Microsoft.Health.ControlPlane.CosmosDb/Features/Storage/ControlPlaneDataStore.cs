// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
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
        private readonly RetryExceptionPolicyFactory _retryExceptionPolicyFactory;
        private readonly HardDeleteRole _hardDeleteRole;

        public ControlPlaneDataStore(
            IScoped<IDocumentClient> documentClient,
            CosmosDataStoreConfiguration cosmosDataStoreConfiguration,
            ICosmosDocumentQueryFactory cosmosDocumentQueryFactory,
            IOptionsMonitor<CosmosCollectionConfiguration> namedCosmosCollectionConfigurationAccessor,
            RetryExceptionPolicyFactory retryExceptionPolicyFactory,
            ILogger<ControlPlaneDataStore> logger)
        {
            EnsureArg.IsNotNull(documentClient, nameof(documentClient));
            EnsureArg.IsNotNull(cosmosDataStoreConfiguration, nameof(cosmosDataStoreConfiguration));
            EnsureArg.IsNotNull(cosmosDocumentQueryFactory, nameof(cosmosDocumentQueryFactory));
            EnsureArg.IsNotNull(namedCosmosCollectionConfigurationAccessor, nameof(namedCosmosCollectionConfigurationAccessor));
            EnsureArg.IsNotNull(retryExceptionPolicyFactory, nameof(retryExceptionPolicyFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            var collectionConfig = namedCosmosCollectionConfigurationAccessor.Get(Constants.CollectionConfigurationName);

            _documentClient = documentClient;
            _collectionUri = cosmosDataStoreConfiguration.GetRelativeCollectionUri(collectionConfig.CollectionId);
            _retryExceptionPolicyFactory = retryExceptionPolicyFactory;
            _collectionId = collectionConfig.CollectionId;
            _databaseId = cosmosDataStoreConfiguration.DatabaseId;
            _cosmosDocumentQueryFactory = cosmosDocumentQueryFactory;
            _logger = logger;
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

        public async Task<string> DeleteRoleAsync(string name, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(name, nameof(name));

            var response = await DeleteSystemDocumentByIdAsync<CosmosRole>(name, CosmosRole.RolePartition, cancellationToken);

            if (response == null)
            {
                throw new RoleNotFoundException(name);
            }

            return response;
        }

        public async System.Threading.Tasks.Task HardDeleteRoleAsync(string name, CancellationToken cancellationToken = default(CancellationToken))
        {
            EnsureArg.IsNotNull(name, nameof(name));

            try
            {
                StoredProcedureResponse<IList<string>> response = await _retryExceptionPolicyFactory.CreateRetryPolicy().ExecuteAsync(
                    async () => await _hardDeleteRole.Execute(
                        _documentClient,
                        _collectionUri,
                        CosmosRole.RolePartition,
                        name),
                    cancellationToken);

                _logger.LogDebug($"Hard-deleted {response.Response.Count} documents, which consumed {response.RequestCharge} RUs. The list of hard-deleted documents: {string.Join(", ", response.Response)}.");
            }
            catch (DocumentClientException dce)
            {
                if (dce.Error?.Message?.Contains(GetValue(HttpStatusCode.RequestEntityTooLarge), StringComparison.Ordinal) == true)
                {
                    // TODO: Eventually, we might want to have our own RequestTooLargeException?
                    throw new Exception();
                }

                _logger.LogError(dce, "Unhandled Document Client Exception");

                throw;
            }
        }

        private static string GetValue(HttpStatusCode type)
        {
            return ((int)type).ToString();
        }

        public async Task<IEnumerable<Role>> GetRoleAllAsync(CancellationToken cancellationToken)
        {
            var role = await GetSystemDocumentAllAsync<CosmosRole>(CosmosRole.RolePartition, cancellationToken);

            return role.ToRoleList();
        }

        public async Task<Role> UpsertRoleAsync(Role role, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(role, nameof(role));
            ValidateRole(role);
            var cosmosRole = new CosmosRole(role);
            var resultRole = await UpsertSystemObjectAsync(cosmosRole, CosmosRole.RolePartition, cancellationToken);
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
                FeedResponse<dynamic> response = await cosmosDocumentQuery.ExecuteNextAsync(cancellationToken);
                var result = response.SingleOrDefault();
                if (result == null)
                {
                    _logger.LogInformation($"{typeof(T)} with id {id} was not found in Cosmos DB.");
                }

                return result;
            }
        }

        private async Task<IEnumerable<T>> GetSystemDocumentAllAsync<T>(string partitionKey, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(partitionKey, nameof(partitionKey));

            var documentQuery = new SqlQuerySpec(
                "SELECT * FROM root");

            var feedOptions = new FeedOptions
            {
                PartitionKey = new PartitionKey(partitionKey),
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
                    _logger.LogInformation("Results were not found in Cosmos DB.");
                }

                return result;
            }
        }

        private async Task<T> UpsertSystemObjectAsync<T>(T systemObject, string partitionKey, CancellationToken cancellationToken)
            where T : class
        {
            EnsureArg.IsNotNull(systemObject, nameof(systemObject));
            EnsureArg.IsNotNull(partitionKey, nameof(partitionKey));
            var eTagAccessCondition = new AccessCondition();

            var requestOptions = new RequestOptions
            {
                PartitionKey = new PartitionKey(partitionKey),
                AccessCondition = eTagAccessCondition,
            };
            try
            {
                ResourceResponse<Document> response = await _documentClient.Value.UpsertDocumentAsync(
                   _collectionUri,
                   systemObject,
                   requestOptions,
                   true,
                   cancellationToken);
                _logger.LogInformation("Request charge: {RequestCharge}, latency: {RequestLatency}", response.RequestCharge, response.RequestLatency);
                return (T)(dynamic)response.Resource;
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

        private async Task<string> DeleteSystemDocumentByIdAsync<T>(string id, string partitionKey, CancellationToken cancellationToken)
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

                await _documentClient.Value.DeleteDocumentAsync(documentUri.ToString(), requestOptions, cancellationToken);

                return "success";
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

        private void ValidateRole(Role role)
        {
            var issues = new List<OperationOutcome.IssueComponent>();

            foreach (var validationError in role.Validate(new ValidationContext(role)))
            {
                issues.Add(new OperationOutcome.IssueComponent
                {
                    Severity = OperationOutcome.IssueSeverity.Fatal,
                    Code = OperationOutcome.IssueType.Invalid,
                    Diagnostics = validationError.ErrorMessage,
                });
            }
        }
    }
}
