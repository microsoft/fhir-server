// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.CosmosDb.Configs;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Continuation;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Security;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.StoredProcedures.HardDelete;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.StoredProcedures.Upsert;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public sealed class CosmosDataStore : IDataStore, IContinuationTokenCache, IProvideCapability, ISecurityDataStore
    {
        private readonly IDocumentClient _documentClient;
        private readonly ICosmosDocumentQueryFactory _cosmosDocumentQueryFactory;
        private readonly ILogger<CosmosDataStore> _logger;
        private readonly Uri _collectionUri;
        private readonly UpsertWithHistory _upsertWithHistoryProc;
        private readonly HardDelete _hardDelete;
        private readonly CosmosDataStoreConfiguration _cosmosDataStoreConfiguration;

        /// <summary>
        /// Initializes a new instance of the <see cref="CosmosDataStore"/> class.
        /// </summary>
        /// <param name="documentClientFactory">
        /// A function that returns an <see cref="IDocumentClient"/>.
        /// Note that this is a function so that the lifetime of the instance is not directly controlled by the IoC container.
        /// </param>
        /// <param name="cosmosDataStoreConfiguration">The data store configuration</param>
        /// <param name="cosmosDocumentQueryFactory">The factory used to create the document query.</param>
        /// <param name="logger">The logger instance.</param>
        public CosmosDataStore(
            Func<IDocumentClient> documentClientFactory,
            CosmosDataStoreConfiguration cosmosDataStoreConfiguration,
            ICosmosDocumentQueryFactory cosmosDocumentQueryFactory,
            ILogger<CosmosDataStore> logger)
        {
            EnsureArg.IsNotNull(documentClientFactory, nameof(documentClientFactory));
            EnsureArg.IsNotNull(cosmosDataStoreConfiguration, nameof(cosmosDataStoreConfiguration));
            EnsureArg.IsNotNull(cosmosDocumentQueryFactory, nameof(cosmosDocumentQueryFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _documentClient = documentClientFactory.Invoke();
            if (_documentClient == null)
            {
                throw new ArgumentException("null was returned by the factory", nameof(documentClientFactory));
            }

            _cosmosDataStoreConfiguration = cosmosDataStoreConfiguration;
            _cosmosDocumentQueryFactory = cosmosDocumentQueryFactory;
            _logger = logger;
            _collectionUri = cosmosDataStoreConfiguration.RelativeCollectionUri;
            _upsertWithHistoryProc = new UpsertWithHistory();
            _hardDelete = new HardDelete();
        }

        public async Task<UpsertOutcome> UpsertAsync(
            ResourceWrapper resource,
            WeakETag weakETag,
            bool allowCreate,
            bool keepHistory,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var cosmosWrapper = new CosmosResourceWrapper(resource);

            try
            {
                _logger.LogDebug($"Upserting {resource.ResourceTypeName}/{resource.ResourceId}, ETag: \"{weakETag?.VersionId}\", AllowCreate: {allowCreate}, KeepHistory: {keepHistory}");

                UpsertWithHistoryModel response = await RetryExceptionPolicy.CreateRetryPolicy().ExecuteAsync(
                    async () => await _upsertWithHistoryProc.Execute(
                        _documentClient,
                        _collectionUri,
                        cosmosWrapper,
                        weakETag?.VersionId,
                        allowCreate,
                        keepHistory),
                    cancellationToken);

                return new UpsertOutcome(response.Wrapper, response.OutcomeType);
            }
            catch (DocumentClientException dce)
            {
                // All errors from a sp (e.g. "pre-condition") come back as a "BadRequest"

                // The ETag does not match the ETag in the document DB database.
                if (dce.Error?.Message?.Contains(GetValue(HttpStatusCode.PreconditionFailed), StringComparison.Ordinal) == true)
                {
                    throw new ResourceConflictException(weakETag);
                }
                else if (dce.Error?.Message?.Contains(GetValue(HttpStatusCode.NotFound), StringComparison.Ordinal) == true)
                {
                    if (weakETag != null)
                    {
                        throw new ResourceConflictException(weakETag);
                    }
                    else if (!allowCreate)
                    {
                        throw new MethodNotAllowedException(Core.Resources.ResourceCreationNotAllowed);
                    }
                }
                else if (dce.Error?.Message?.Contains(GetValue(HttpStatusCode.ServiceUnavailable), StringComparison.Ordinal) == true)
                {
                    throw new ServiceUnavailableException();
                }

                _logger.LogError(dce, "Unhandled Document Client Exception");

                throw;
            }
        }

        public async Task<ResourceWrapper> GetAsync(ResourceKey key, CancellationToken cancellationToken = default(CancellationToken))
        {
            EnsureArg.IsNotNull(key, nameof(key));

            var id = !string.IsNullOrEmpty(key.VersionId) ? $"{key.Id}_{key.VersionId}" : key.Id;

            _logger.LogDebug($"Get {key.ResourceType}/{id}");

            var sqlParameterCollection = new SqlParameterCollection(new[]
            {
                new SqlParameter("@id", id),
                new SqlParameter("@resourceId", key.Id),
                new SqlParameter("@version", key.VersionId),
            });

            var sqlQuerySpec = new SqlQuerySpec("select * from root r where r.id = @id or (r.id = @resourceId and r.version = @version)", sqlParameterCollection);

            var executor = CreateDocumentQuery<CosmosResourceWrapper>(
                sqlQuerySpec,
                new FeedOptions { PartitionKey = new PartitionKey(key.ToPartitionKey()) });

            var result = await executor.ExecuteNextAsync<CosmosResourceWrapper>(cancellationToken);

            return result.FirstOrDefault();
        }

        public async Task HardDeleteAsync(ResourceKey key, CancellationToken cancellationToken = default(CancellationToken))
        {
            EnsureArg.IsNotNull(key, nameof(key));

            try
            {
                _logger.LogDebug($"Obliterating {key.ResourceType}/{key.Id}");

                StoredProcedureResponse<IList<string>> response = await RetryExceptionPolicy.CreateRetryPolicy().ExecuteAsync(
                    async () => await _hardDelete.Execute(
                        _documentClient,
                        _collectionUri,
                        key),
                    cancellationToken);

                _logger.LogDebug($"Hard-deleted {response.Response.Count} documents, which consumed {response.RequestCharge} RUs. The list of hard-deleted documents: {string.Join(", ", response.Response)}.");
            }
            catch (DocumentClientException dce)
            {
                if (dce.Error?.Message?.Contains(GetValue(HttpStatusCode.RequestEntityTooLarge), StringComparison.Ordinal) == true)
                {
                    // TODO: Eventually, we might want to have our own RequestTooLargeException?
                    throw new ServiceUnavailableException();
                }

                _logger.LogError(dce, "Unhandled Document Client Exception");

                throw;
            }
        }

        internal IDocumentQuery<T> CreateDocumentQuery<T>(
            SqlQuerySpec sqlQuerySpec,
            FeedOptions feedOptions = null)
        {
            EnsureArg.IsNotNull(sqlQuerySpec, nameof(sqlQuerySpec));

            CosmosQueryContext context = new CosmosQueryContext(_collectionUri, sqlQuerySpec, feedOptions);

            return _cosmosDocumentQueryFactory.Create<T>(context);
        }

        public async Task<string> GetContinuationTokenAsync(string id, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(id, nameof(id));

            var result = await GetSystemDocumentByIdAsync<ContinuationToken>(id, ContinuationToken.ContinuationTokenPartition, cancellationToken);

            if (result == null)
            {
                throw new InvalidSearchOperationException(Core.Resources.InvalidContinuationToken);
            }

            return result.Token;
        }

        public async Task<string> SaveContinuationTokenAsync(string continuationToken, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(continuationToken, nameof(continuationToken));

            var savedCt = new ContinuationToken(continuationToken);

            await UpsertSystemObjectAsync(savedCt, ContinuationToken.ContinuationTokenPartition);

            return savedCt.Id;
        }

        public async Task<IEnumerable<Role>> GetAllRolesAsync(CancellationToken cancellationToken)
        {
            var roleQuery = new SqlQuerySpec(
                "SELECT * FROM root r");

            var feedOptions = new FeedOptions
            {
                PartitionKey = new PartitionKey(CosmosRole.RolePartition),
            };

            IDocumentQuery<CosmosRole> cosmosDocumentQuery = CreateDocumentQuery<CosmosRole>(roleQuery, feedOptions);

            var roles = new List<Role>();

            using (cosmosDocumentQuery)
            {
                while (cosmosDocumentQuery.HasMoreResults)
                {
                    var response = await cosmosDocumentQuery.ExecuteNextAsync<CosmosRole>(cancellationToken);
                    roles.AddRange(response.Select(x => x.ToRole()));
                }
            }

            return roles;
        }

        public async Task<Role> GetRoleAsync(string name, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(name, nameof(name));

            var role = await GetSystemDocumentByIdAsync<CosmosRole>(name, CosmosRole.RolePartition, cancellationToken);

            if (role == null)
            {
                throw new InvalidSearchOperationException(Core.Resources.InvalidRoleName);
            }

            return role.ToRole();
        }

        public async Task<Role> UpsertRoleAsync(Role role, WeakETag weakETag, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(role, nameof(role));

            var cosmosRole = new CosmosRole(role);

            var resultRole = await UpsertSystemObjectAsync(cosmosRole, CosmosRole.RolePartition, weakETag);

            return resultRole.ToRole();
        }

        public async Task DeleteRoleAsync(string name, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(name, nameof(name));

            _logger.LogDebug($"Obliterating {name}");

            var documentUri = UriFactory.CreateDocumentUri(
                _cosmosDataStoreConfiguration.DatabaseId,
                _cosmosDataStoreConfiguration.CollectionId,
                name);

            await _documentClient.DeleteDocumentAsync(
                documentUri,
                new RequestOptions
                {
                    PartitionKey = new PartitionKey(CosmosRole.RolePartition),
                });
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

        private async Task<T> UpsertSystemObjectAsync<T>(T systemObject, string partitionKey, WeakETag weakETag = null)
            where T : class
        {
            EnsureArg.IsNotNull(systemObject, nameof(systemObject));

            var eTagAccessCondition = new AccessCondition();
            if (weakETag != null)
            {
                eTagAccessCondition.Condition = weakETag.VersionId;
                eTagAccessCondition.Type = AccessConditionType.IfMatch;
            }

            using (_logger.BeginTimedScope($"{nameof(CosmosDataStore)}.{nameof(UpsertSystemObjectAsync)}"))
            {
                var systemStopwatch = new Stopwatch();
                systemStopwatch.Start();

                var requestOptions = new RequestOptions
                {
                    PartitionKey = new PartitionKey(partitionKey),
                    AccessCondition = eTagAccessCondition,
                };

                try
                {
                    var response = await _documentClient.UpsertDocumentAsync(
                        _collectionUri,
                        systemObject,
                        requestOptions,
                        true);

                    _logger.LogInformation("Request charge: {RequestCharge}, latency: {RequestLatency}", response.RequestCharge, response.RequestLatency);

                    return (dynamic)response.Resource;
                }
                catch (DocumentClientException dce)
                {
                    if (string.Equals(dce.Error.Code, HttpStatusCode.PreconditionFailed.ToString(), StringComparison.InvariantCultureIgnoreCase))
                    {
                        throw new ResourceConflictException(weakETag);
                    }

                    _logger.LogError(dce, "Unhandled Document Client Exception");

                    throw;
                }
            }
        }

        private static string GetValue(HttpStatusCode type)
        {
            return ((int)type).ToString();
        }

        public void Build(ListedCapabilityStatement statement)
        {
            EnsureArg.IsNotNull(statement, nameof(statement));

            foreach (var resource in ModelInfo.SupportedResources)
            {
                var resourceType = (ResourceType)Enum.Parse(typeof(ResourceType), resource);
                statement.BuildRestResourceComponent(resourceType, builder =>
                {
                    builder.Versioning.Add(CapabilityStatement.ResourceVersionPolicy.NoVersion);
                    builder.Versioning.Add(CapabilityStatement.ResourceVersionPolicy.Versioned);
                    builder.Versioning.Add(CapabilityStatement.ResourceVersionPolicy.VersionedUpdate);
                    builder.ReadHistory = true;
                    builder.UpdateCreate = true;
                });
            }
        }
    }
}
