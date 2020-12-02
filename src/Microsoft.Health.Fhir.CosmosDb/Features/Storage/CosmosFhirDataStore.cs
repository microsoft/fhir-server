// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Scripts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Core;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.CosmosDb.Configs;
using Microsoft.Health.Fhir.CosmosDb.Features.Search;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.StoredProcedures.HardDelete;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.StoredProcedures.Replace;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.StoredProcedures.Upsert;
using Microsoft.Health.Fhir.ValueSets;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public sealed class CosmosFhirDataStore : IFhirDataStore, IProvideCapability
    {
        private readonly IScoped<Container> _containerScope;
        private readonly CosmosDataStoreConfiguration _cosmosDataStoreConfiguration;
        private readonly ICosmosQueryFactory _cosmosQueryFactory;
        private readonly RetryExceptionPolicyFactory _retryExceptionPolicyFactory;
        private readonly ILogger<CosmosFhirDataStore> _logger;

        private static readonly UpsertWithHistory _upsertWithHistoryProc = new UpsertWithHistory();
        private static readonly HardDelete _hardDelete = new HardDelete();
        private static readonly ReplaceSingleResource _replaceSingleResource = new ReplaceSingleResource();
        private readonly CoreFeatureConfiguration _coreFeatures;

        /// <summary>
        /// Initializes a new instance of the <see cref="CosmosFhirDataStore"/> class.
        /// </summary>
        /// <param name="containerScope">
        /// A function that returns an <see cref="Container"/>.
        /// Note that this is a function so that the lifetime of the instance is not directly controlled by the IoC container.
        /// </param>
        /// <param name="cosmosDataStoreConfiguration">The data store configuration.</param>
        /// <param name="namedCosmosCollectionConfigurationAccessor">The IOptions accessor to get a named version.</param>
        /// <param name="cosmosQueryFactory">The factory used to create the document query.</param>
        /// <param name="retryExceptionPolicyFactory">The retry exception policy factory.</param>
        /// <param name="logger">The logger instance.</param>
        /// <param name="coreFeatures">The core feature configuration</param>
        public CosmosFhirDataStore(
            IScoped<Container> containerScope,
            CosmosDataStoreConfiguration cosmosDataStoreConfiguration,
            IOptionsMonitor<CosmosCollectionConfiguration> namedCosmosCollectionConfigurationAccessor,
            ICosmosQueryFactory cosmosQueryFactory,
            RetryExceptionPolicyFactory retryExceptionPolicyFactory,
            ILogger<CosmosFhirDataStore> logger,
            IOptions<CoreFeatureConfiguration> coreFeatures)
        {
            EnsureArg.IsNotNull(containerScope, nameof(containerScope));
            EnsureArg.IsNotNull(cosmosDataStoreConfiguration, nameof(cosmosDataStoreConfiguration));
            EnsureArg.IsNotNull(namedCosmosCollectionConfigurationAccessor, nameof(namedCosmosCollectionConfigurationAccessor));
            EnsureArg.IsNotNull(cosmosQueryFactory, nameof(cosmosQueryFactory));
            EnsureArg.IsNotNull(retryExceptionPolicyFactory, nameof(retryExceptionPolicyFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(coreFeatures, nameof(coreFeatures));

            _containerScope = containerScope;
            _cosmosDataStoreConfiguration = cosmosDataStoreConfiguration;
            _cosmosQueryFactory = cosmosQueryFactory;
            _retryExceptionPolicyFactory = retryExceptionPolicyFactory;
            _logger = logger;
            _coreFeatures = coreFeatures.Value;
        }

        public async Task<UpsertOutcome> UpsertAsync(
            ResourceWrapper resource,
            WeakETag weakETag,
            bool allowCreate,
            bool keepHistory,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var cosmosWrapper = new FhirCosmosResourceWrapper(resource);

            try
            {
                _logger.LogDebug($"Upserting {resource.ResourceTypeName}/{resource.ResourceId}, ETag: \"{weakETag?.VersionId}\", AllowCreate: {allowCreate}, KeepHistory: {keepHistory}");

                UpsertWithHistoryModel response = await _retryExceptionPolicyFactory.CreateRetryPolicy().ExecuteAsync(
                    async ct => await _upsertWithHistoryProc.Execute(
                        _containerScope.Value.Scripts,
                        cosmosWrapper,
                        weakETag?.VersionId,
                        allowCreate,
                        keepHistory,
                        ct),
                    cancellationToken);

                return new UpsertOutcome(response.Wrapper, response.OutcomeType);
            }
            catch (CosmosException exception)
            {
                switch (exception.GetSubStatusCode())
                {
                    case HttpStatusCode.PreconditionFailed:
                        throw new PreconditionFailedException(string.Format(Core.Resources.ResourceVersionConflict, weakETag?.VersionId));
                    case HttpStatusCode.NotFound:
                        if (cosmosWrapper.IsDeleted)
                        {
                            return null;
                        }

                        if (weakETag != null)
                        {
                            throw new ResourceNotFoundException(string.Format(Core.Resources.ResourceNotFoundByIdAndVersion, resource.ResourceTypeName, resource.ResourceId, weakETag.VersionId));
                        }
                        else if (!allowCreate)
                        {
                            throw new MethodNotAllowedException(Core.Resources.ResourceCreationNotAllowed);
                        }

                        break;

                    case HttpStatusCode.ServiceUnavailable:
                        throw new ServiceUnavailableException();
                }

                _logger.LogError(exception, "Unhandled Document Client Exception");

                throw;
            }
        }

        public async Task<ResourceWrapper> GetAsync(ResourceKey key, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(key, nameof(key));

            bool isVersionedRead = !string.IsNullOrEmpty(key.VersionId);

            if (isVersionedRead)
            {
                QueryDefinition sqlQuerySpec = new QueryDefinition($"select {SearchValueConstants.SelectedFields} from root r where r.resourceId = @resourceId and r.version = @version")
                    .WithParameter("@resourceId", key.Id)
                    .WithParameter("@version", key.VersionId);

                (IReadOnlyList<FhirCosmosResourceWrapper> results, _) = await ExecuteDocumentQueryAsync<FhirCosmosResourceWrapper>(
                    sqlQuerySpec,
                    new QueryRequestOptions { PartitionKey = new PartitionKey(key.ToPartitionKey()) },
                    cancellationToken: cancellationToken);

                return results.Count == 0 ? null : results[0];
            }

            try
            {
                return await _containerScope.Value
                    .ReadItemAsync<FhirCosmosResourceWrapper>(key.Id, new PartitionKey(key.ToPartitionKey()), cancellationToken: cancellationToken);
            }
            catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public async Task HardDeleteAsync(ResourceKey key, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(key, nameof(key));

            try
            {
                _logger.LogDebug($"Obliterating {key.ResourceType}/{key.Id}");

                StoredProcedureExecuteResponse<IList<string>> response = await _retryExceptionPolicyFactory.CreateRetryPolicy().ExecuteAsync(
                    async ct => await _hardDelete.Execute(
                        _containerScope.Value.Scripts,
                        key,
                        ct),
                    cancellationToken);

                _logger.LogDebug($"Hard-deleted {response.Resource.Count} documents, which consumed {response.RequestCharge} RUs. The list of hard-deleted documents: {string.Join(", ", response.Resource)}.");
            }
            catch (CosmosException exception)
            {
                if (exception.IsRequestEntityTooLarge())
                {
                    throw;
                }

                _logger.LogError(exception, "Unhandled Document Client Exception");

                throw;
            }
        }

        public async Task UpdateSearchParameterHashBatchAsync(IReadOnlyCollection<ResourceWrapper> resources, CancellationToken cancellationToken)
        {
            // TODO: use batch command to update both hash values and search index values for list updateSearchIndices
            // this is a place holder update until we batch update resources
            foreach (var resource in resources)
            {
                await UpdateSearchIndexForResourceAsync(resource, WeakETag.FromVersionId(resource.Version), cancellationToken);
            }
        }

        public async Task UpdateSearchParameterIndicesBatchAsync(IReadOnlyCollection<ResourceWrapper> resources, CancellationToken cancellationToken)
        {
            // TODO: use batch command to update both hash values and search index values for list updateSearchIndices
            // this is a place holder update until we batch update resources
            foreach (var resource in resources)
            {
                await UpdateSearchIndexForResourceAsync(resource, WeakETag.FromVersionId(resource.Version), cancellationToken);
            }
        }

        public async Task<ResourceWrapper> UpdateSearchIndexForResourceAsync(ResourceWrapper resourceWrapper, WeakETag weakETag, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resourceWrapper, nameof(resourceWrapper));
            EnsureArg.IsNotNull(weakETag, nameof(weakETag));

            var cosmosWrapper = new FhirCosmosResourceWrapper(resourceWrapper);

            try
            {
                _logger.LogDebug($"Replacing {resourceWrapper.ResourceTypeName}/{resourceWrapper.ResourceId}, ETag: \"{weakETag.VersionId}\"");

                FhirCosmosResourceWrapper response = await _retryExceptionPolicyFactory.CreateRetryPolicy().ExecuteAsync(
                    async ct => await _replaceSingleResource.Execute(
                        _containerScope.Value.Scripts,
                        cosmosWrapper,
                        weakETag.VersionId,
                        ct),
                    cancellationToken);

                return response;
            }
            catch (CosmosException exception)
            {
                // Check GetSubStatusCode documentation for why we need to get that instead of the status code.
                switch (exception.GetSubStatusCode())
                {
                    case HttpStatusCode.PreconditionFailed:
                        throw new PreconditionFailedException(string.Format(Core.Resources.ResourceVersionConflict, weakETag));

                    case HttpStatusCode.NotFound:
                        throw new ResourceNotFoundException(string.Format(
                            Core.Resources.ResourceNotFoundByIdAndVersion,
                            resourceWrapper.ResourceTypeName,
                            resourceWrapper.ResourceId,
                            weakETag));

                    case HttpStatusCode.ServiceUnavailable:
                        throw new ServiceUnavailableException();
                }

                _logger.LogError(exception, "Unhandled Document Client Exception");
                throw;
            }
        }

        /// <summary>
        /// Executes a query. If <see cref="FeedOptions.MaxItemCount"/> is set, iterates through the pages returned by Cosmos DB until the result set
        /// has at least half that many results. Paging though subsequent pages times out after <see cref="CosmosDataStoreConfiguration.SearchEnumerationTimeoutInSeconds"/>
        /// or after a 429 response from the DB.
        /// </summary>
        /// <typeparam name="T">The result entry type.</typeparam>
        /// <param name="sqlQuerySpec">The query specification.</param>
        /// <param name="feedOptions">The feed options.</param>
        /// <param name="continuationToken">The continuation token from a previous query.</param>
        /// <param name="mustNotExceedMaxItemCount">If set to true, no more than <see cref="FeedOptions.MaxItemCount"/> entries will be returned. Otherwise, up to 2 * MaxItemCount - 1 items could be returned</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The results and possible continuation token</returns>
        internal async Task<(IReadOnlyList<T> results, string continuationToken)> ExecuteDocumentQueryAsync<T>(QueryDefinition sqlQuerySpec, QueryRequestOptions feedOptions, string continuationToken = null, bool mustNotExceedMaxItemCount = true, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(sqlQuerySpec, nameof(sqlQuerySpec));

            var context = new CosmosQueryContext(sqlQuerySpec, feedOptions, continuationToken);
            var cosmosQuery = _cosmosQueryFactory.Create<T>(_containerScope.Value, context);
            var startTime = Clock.UtcNow;

            FeedResponse<T> page = await cosmosQuery.ExecuteNextAsync(cancellationToken);

            if (!cosmosQuery.HasMoreResults || !feedOptions.MaxItemCount.HasValue || page.Count == feedOptions.MaxItemCount)
            {
                if (page.Count == 0)
                {
                    return (Array.Empty<T>(), page.ContinuationToken);
                }

                var singlePageResults = new List<T>(page.Count);
                singlePageResults.AddRange(page);
                return (singlePageResults, page.ContinuationToken);
            }

            int totalDesiredCount = feedOptions.MaxItemCount.Value;

            // try to obtain at least half of the requested results

            using var timeoutTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(_cosmosDataStoreConfiguration.SearchEnumerationTimeoutInSeconds) - (Clock.UtcNow - startTime));
            using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutTokenSource.Token);

            var results = new List<T>(totalDesiredCount);
            results.AddRange(page);

            while (cosmosQuery.HasMoreResults && results.Count < totalDesiredCount / 2)
            {
                // The FHIR spec says we cannot return more items in a bundle than the _count parameter, if specified.
                // If not specified, mustNotExceedMaxItemCount will be false, and we can allow ourselves to go over the limit.
                // The advantage is that we don't need to construct a new query with a new page size.

                int currentDesiredCount = totalDesiredCount - results.Count;
                if (mustNotExceedMaxItemCount && currentDesiredCount != feedOptions.MaxItemCount)
                {
                    // Construct a new query with a smaller page size.
                    // We do this to ensure that we will not exceed the original max page size and that
                    // we never have to throw a page of data away because it won't fit in the response.
                    feedOptions.MaxItemCount = currentDesiredCount;
                    context = new CosmosQueryContext(sqlQuerySpec, feedOptions, page.ContinuationToken);
                    cosmosQuery = _cosmosQueryFactory.Create<T>(_containerScope.Value, context);
                }

                try
                {
                    page = await cosmosQuery.ExecuteNextAsync(linkedTokenSource.Token);
                    if (page.Count > 0)
                    {
                        results.AddRange(page);
                    }
                }
                catch (CosmosException e) when (e.IsRequestRateExceeded())
                {
                    // return whatever we have when we get a 429
                    break;
                }
                catch (OperationCanceledException) when (timeoutTokenSource.IsCancellationRequested)
                {
                    // This took too long. Give up.
                    break;
                }
            }

            return (results, page.ContinuationToken);
        }

        public void Build(ICapabilityStatementBuilder builder)
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            builder.AddDefaultResourceInteractions()
                .AddDefaultSearchParameters()
                .AddDefaultRestSearchParams();

            if (_coreFeatures.SupportsBatch)
            {
                builder.AddRestInteraction(SystemRestfulInteraction.Batch);
            }
        }
    }
}
