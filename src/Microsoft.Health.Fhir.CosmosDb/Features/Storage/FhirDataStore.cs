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
using Hl7.Fhir.Model;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.CosmosDb.Configs;
using Microsoft.Health.CosmosDb.Features.Storage;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Export;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.StoredProcedures.HardDelete;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.StoredProcedures.Upsert;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public sealed class FhirDataStore : IDataStore, IProvideCapability
    {
        private readonly IDocumentClient _documentClient;
        private readonly CosmosDataStoreConfiguration _cosmosDataStoreConfiguration;
        private readonly CosmosCollectionConfiguration _collectionConfiguration;
        private readonly ICosmosDocumentQueryFactory _cosmosDocumentQueryFactory;
        private readonly RetryExceptionPolicyFactory _retryExceptionPolicyFactory;
        private readonly ILogger<FhirDataStore> _logger;
        private readonly Uri _collectionUri;
        private readonly UpsertWithHistory _upsertWithHistoryProc;
        private readonly HardDelete _hardDelete;

        /// <summary>
        /// Initializes a new instance of the <see cref="FhirDataStore"/> class.
        /// </summary>
        /// <param name="documentClient">
        /// A function that returns an <see cref="IDocumentClient"/>.
        /// Note that this is a function so that the lifetime of the instance is not directly controlled by the IoC container.
        /// </param>
        /// <param name="cosmosDataStoreConfiguration">The data store configuration</param>
        /// <param name="cosmosDocumentQueryFactory">The factory used to create the document query.</param>
        /// <param name="retryExceptionPolicyFactory">The retry exception policy factory.</param>
        /// <param name="fhirRequestContextAccessor">The fhir request context accessor.</param>
        /// <param name="namedCosmosCollectionConfigurationAccessor">The IOptions accessor to get a named version.</param>
        /// <param name="logger">The logger instance.</param>
        public FhirDataStore(
            IScoped<IDocumentClient> documentClient,
            CosmosDataStoreConfiguration cosmosDataStoreConfiguration,
            FhirCosmosDocumentQueryFactory cosmosDocumentQueryFactory,
            RetryExceptionPolicyFactory retryExceptionPolicyFactory,
            IFhirRequestContextAccessor fhirRequestContextAccessor,
            IOptionsMonitor<CosmosCollectionConfiguration> namedCosmosCollectionConfigurationAccessor,
            ILogger<FhirDataStore> logger)
        {
            EnsureArg.IsNotNull(documentClient, nameof(documentClient));
            EnsureArg.IsNotNull(cosmosDataStoreConfiguration, nameof(cosmosDataStoreConfiguration));
            EnsureArg.IsNotNull(cosmosDocumentQueryFactory, nameof(cosmosDocumentQueryFactory));
            EnsureArg.IsNotNull(retryExceptionPolicyFactory, nameof(retryExceptionPolicyFactory));
            EnsureArg.IsNotNull(namedCosmosCollectionConfigurationAccessor, nameof(namedCosmosCollectionConfigurationAccessor));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _collectionConfiguration = namedCosmosCollectionConfigurationAccessor.Get(Constants.CollectionConfigurationName);

            _cosmosDocumentQueryFactory = cosmosDocumentQueryFactory;
            _retryExceptionPolicyFactory = retryExceptionPolicyFactory;
            _logger = logger;
            _documentClient = new FhirDocumentClient(documentClient.Value, fhirRequestContextAccessor, cosmosDataStoreConfiguration.ContinuationTokenSizeLimitInKb);
            _cosmosDataStoreConfiguration = cosmosDataStoreConfiguration;
            _collectionUri = cosmosDataStoreConfiguration.GetRelativeCollectionUri(_collectionConfiguration.CollectionId);
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

            var cosmosWrapper = new FhirCosmosResourceWrapper(resource);

            try
            {
                _logger.LogDebug($"Upserting {resource.ResourceTypeName}/{resource.ResourceId}, ETag: \"{weakETag?.VersionId}\", AllowCreate: {allowCreate}, KeepHistory: {keepHistory}");

                UpsertWithHistoryModel response = await _retryExceptionPolicyFactory.CreateRetryPolicy().ExecuteAsync(
                    async ct => await _upsertWithHistoryProc.Execute(
                        _documentClient,
                        _collectionUri,
                        cosmosWrapper,
                        weakETag?.VersionId,
                        allowCreate,
                        keepHistory,
                        ct),
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

            bool isVersionedRead = !string.IsNullOrEmpty(key.VersionId);

            if (isVersionedRead)
            {
                var sqlParameterCollection = new SqlParameterCollection(new[]
                {
                    new SqlParameter("@resourceId", key.Id),
                    new SqlParameter("@version", key.VersionId),
                });

                var sqlQuerySpec = new SqlQuerySpec("select * from root r where r.resourceId = @resourceId and r.version = @version", sqlParameterCollection);

                var executor = CreateDocumentQuery<FhirCosmosResourceWrapper>(
                    sqlQuerySpec,
                    new FeedOptions { PartitionKey = new PartitionKey(key.ToPartitionKey()) });

                var result = await executor.ExecuteNextAsync<FhirCosmosResourceWrapper>(cancellationToken);

                return result.FirstOrDefault();
            }

            try
            {
                return await _documentClient.ReadDocumentAsync<FhirCosmosResourceWrapper>(
                    UriFactory.CreateDocumentUri(_cosmosDataStoreConfiguration.DatabaseId, _collectionConfiguration.CollectionId, key.Id),
                    new RequestOptions { PartitionKey = new PartitionKey(key.ToPartitionKey()) },
                    cancellationToken);
            }
            catch (DocumentClientException e) when (e.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public async Task HardDeleteAsync(ResourceKey key, CancellationToken cancellationToken = default(CancellationToken))
        {
            EnsureArg.IsNotNull(key, nameof(key));

            try
            {
                _logger.LogDebug($"Obliterating {key.ResourceType}/{key.Id}");

                StoredProcedureResponse<IList<string>> response = await _retryExceptionPolicyFactory.CreateRetryPolicy().ExecuteAsync(
                    async ct => await _hardDelete.Execute(
                        _documentClient,
                        _collectionUri,
                        key,
                        ct),
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

        public async Task<JobCreationStatus> UpsertExportJobAsync(ExportJobRecord jobRecord, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(jobRecord, nameof(jobRecord));

            var result = await _documentClient.UpsertDocumentAsync(
                _collectionUri,
                jobRecord,
                new RequestOptions() { PartitionKey = new PartitionKey(jobRecord.PartitionKey) },
                disableAutomaticIdGeneration: true,
                cancellationToken: cancellationToken);

            return GetJobCreationStatus(result.StatusCode);
        }

        private JobCreationStatus GetJobCreationStatus(HttpStatusCode httpStatus)
        {
            switch (httpStatus)
            {
                case HttpStatusCode.Created:
                    return JobCreationStatus.Created;
                case HttpStatusCode.OK:
                    return JobCreationStatus.Updated;
                default:
                    return JobCreationStatus.Failed;
            }
        }

        internal IDocumentQuery<T> CreateDocumentQuery<T>(
            SqlQuerySpec sqlQuerySpec,
            FeedOptions feedOptions = null)
        {
            EnsureArg.IsNotNull(sqlQuerySpec, nameof(sqlQuerySpec));

            CosmosQueryContext context = new CosmosQueryContext(_collectionUri, sqlQuerySpec, feedOptions);

            return _cosmosDocumentQueryFactory.Create<T>(_documentClient, context);
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
