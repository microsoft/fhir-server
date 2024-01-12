// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
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
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.CosmosDb.Configs;
using Microsoft.Health.Fhir.CosmosDb.Features.Queries;
using Microsoft.Health.Fhir.CosmosDb.Features.Search;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.StoredProcedures.HardDelete;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.StoredProcedures.Replace;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.IO;
using Polly;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public sealed class CosmosFhirDataStore : IFhirDataStore, IProvideCapability
    {
        private const int MergeAsyncDegreeOfParallelism = -1; // Max degree of parallelism during merge async. -1 will attempt to use all resources available in the system.

        private const int BlobSizeThresholdWarningInBytes = 1000000; // 1MB threshold.

        /// <summary>
        /// The fraction of <see cref="QueryRequestOptions.MaxItemCount"/> to attempt to fill before giving up.
        /// </summary>
        internal const double ExecuteDocumentQueryAsyncMinimumFillFactor = 0.5;
        internal const double ExecuteDocumentQueryAsyncMaximumFillFactor = 10;

        private static readonly HardDelete _hardDelete = new();
        private static readonly ReplaceSingleResource _replaceSingleResource = new();
        private static readonly RecyclableMemoryStreamManager _recyclableMemoryStreamManager = new();

        private readonly IScoped<Container> _containerScope;
        private readonly CosmosDataStoreConfiguration _cosmosDataStoreConfiguration;
        private readonly ICosmosQueryFactory _cosmosQueryFactory;
        private readonly RetryExceptionPolicyFactory _retryExceptionPolicyFactory;
        private readonly ILogger<CosmosFhirDataStore> _logger;
        private readonly Lazy<ISupportedSearchParameterDefinitionManager> _supportedSearchParameters;
        private readonly CoreFeatureConfiguration _coreFeatures;
        private readonly IBundleOrchestrator _bundleOrchestrator;
        private readonly IModelInfoProvider _modelInfoProvider;

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
        /// <param name="bundleOrchestrator">Bundle orchestrator</param>
        /// <param name="supportedSearchParameters">The supported search parameters</param>
        /// <param name="modelInfoProvider">The model info provider to determine the FHIR version when handling resource conflicts.</param>
        public CosmosFhirDataStore(
            IScoped<Container> containerScope,
            CosmosDataStoreConfiguration cosmosDataStoreConfiguration,
            IOptionsMonitor<CosmosCollectionConfiguration> namedCosmosCollectionConfigurationAccessor,
            ICosmosQueryFactory cosmosQueryFactory,
            RetryExceptionPolicyFactory retryExceptionPolicyFactory,
            ILogger<CosmosFhirDataStore> logger,
            IOptions<CoreFeatureConfiguration> coreFeatures,
            IBundleOrchestrator bundleOrchestrator,
            Lazy<ISupportedSearchParameterDefinitionManager> supportedSearchParameters,
            IModelInfoProvider modelInfoProvider)
        {
            EnsureArg.IsNotNull(containerScope, nameof(containerScope));
            EnsureArg.IsNotNull(cosmosDataStoreConfiguration, nameof(cosmosDataStoreConfiguration));
            EnsureArg.IsNotNull(namedCosmosCollectionConfigurationAccessor, nameof(namedCosmosCollectionConfigurationAccessor));
            EnsureArg.IsNotNull(cosmosQueryFactory, nameof(cosmosQueryFactory));
            EnsureArg.IsNotNull(retryExceptionPolicyFactory, nameof(retryExceptionPolicyFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(coreFeatures, nameof(coreFeatures));
            EnsureArg.IsNotNull(bundleOrchestrator, nameof(bundleOrchestrator));
            EnsureArg.IsNotNull(supportedSearchParameters, nameof(supportedSearchParameters));

            _containerScope = containerScope;
            _cosmosDataStoreConfiguration = cosmosDataStoreConfiguration;
            _cosmosQueryFactory = cosmosQueryFactory;
            _retryExceptionPolicyFactory = retryExceptionPolicyFactory;
            _logger = logger;
            _supportedSearchParameters = supportedSearchParameters;
            _coreFeatures = coreFeatures.Value;
            _bundleOrchestrator = bundleOrchestrator;
            _modelInfoProvider = modelInfoProvider;
        }

        public async Task<IReadOnlyList<ResourceWrapper>> GetAsync(IReadOnlyList<ResourceKey> keys, CancellationToken cancellationToken)
        {
            var results = new List<ResourceWrapper>();
            if (keys == null || keys.Count == 0)
            {
                return results;
            }

            results.AddRange(await Task.WhenAll(keys.Select(async key => await GetAsync(key, cancellationToken))));

            return results;
        }

        public async Task<IDictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>> MergeAsync(IReadOnlyList<ResourceWrapperOperation> resources, CancellationToken cancellationToken)
        {
            return await MergeAsync(resources, MergeOptions.Default, cancellationToken);
        }

        public async Task<IDictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>> MergeAsync(IReadOnlyList<ResourceWrapperOperation> resources, MergeOptions mergeOptions, CancellationToken cancellationToken)
        {
            if (resources == null || resources.Count == 0)
            {
                return new Dictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>();
            }

            Stopwatch watch = Stopwatch.StartNew();
            var results = new ConcurrentDictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>();

            await MergeInternalAsync(resources, results, null, cancellationToken);

            KeyValuePair<DataStoreOperationIdentifier, DataStoreOperationOutcome>[] retrySequentially =
                results.Where(x => x.Value.Exception is RequestRateExceededException).ToArray();

            if (retrySequentially.Any())
            {
                ResourceWrapperOperation[] filtered = resources
                    .Where(x => retrySequentially.Any(y => x.GetIdentifier().Equals(y.Key)))
                    .ToArray();

                if (filtered.Any())
                {
                    await MergeInternalAsync(filtered, results, 1, cancellationToken);
                }
            }

            if (resources.Count > 2)
            {
                _logger.LogDebug("CosmosDbMergeAsync - Resource: {CountOfResources} - {ElapsedTime}ms", resources.Count, watch.ElapsedMilliseconds);
            }

            return results;
        }

        private async Task MergeInternalAsync(IReadOnlyList<ResourceWrapperOperation> resources, ConcurrentDictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome> results, int? maxParallelism, CancellationToken cancellationToken)
        {
            var parallelOptions = new ParallelOptions { CancellationToken = cancellationToken };

            if (maxParallelism.HasValue)
            {
                parallelOptions.MaxDegreeOfParallelism = maxParallelism.Value;
            }
            else
            {
                parallelOptions.MaxDegreeOfParallelism = MergeAsyncDegreeOfParallelism;
            }

            await Parallel.ForEachAsync(resources, parallelOptions, async (resource, innerCt) =>
            {
                DataStoreOperationIdentifier identifier = resource.GetIdentifier();
                try
                {
                    UpsertOutcome upsertOutcome = await InternalUpsertAsync(
                            resource.Wrapper,
                            resource.WeakETag,
                            resource.AllowCreate,
                            resource.KeepHistory,
                            innerCt,
                            resource.RequireETagOnUpdate);

                    var result = new DataStoreOperationOutcome(upsertOutcome);
                    results.AddOrUpdate(identifier, _ => result, (_, _) => result);
                }
                catch (RequestRateExceededException rateExceededException)
                {
                    results.TryAdd(identifier, new DataStoreOperationOutcome(rateExceededException));
                }
                catch (FhirException fhirException)
                {
                    // This block catches only FhirExceptions. FhirException can be thrown by the data store layer
                    // in different situations, like: Failed pre-conditions, bad requests, resource not found, etc.

                    results.TryAdd(identifier, new DataStoreOperationOutcome(fhirException));
                }
                catch (Exception ex)
                {
                    throw new IncompleteOperationException<IDictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>>(ex, results);
                }
            });
        }

        public async Task<UpsertOutcome> UpsertAsync(ResourceWrapperOperation resource, CancellationToken cancellationToken)
        {
            bool isBundleOperation = _bundleOrchestrator.IsEnabled && resource.BundleResourceContext != null;

            if (isBundleOperation)
            {
                IBundleOrchestratorOperation operation = _bundleOrchestrator.GetOperation(resource.BundleResourceContext.BundleOperationId);

                // Internally Bundle Operation calls "MergeAsync".
                return await operation.AppendResourceAsync(resource, this, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                try
                {
                    return await InternalUpsertAsync(
                        resource.Wrapper,
                        resource.WeakETag,
                        resource.AllowCreate,
                        resource.KeepHistory,
                        cancellationToken,
                        resource.RequireETagOnUpdate);
                }
                catch (FhirException fhirException)
                {
                    // This block catches only FhirExceptions. FhirException can be thrown by the data store layer
                    // in different situations, like: Failed pre-conditions, bad requests, resource not found, etc.

                    _logger.LogInformation("Upserting failed. {ExceptionType}: {ExceptionMessage}", fhirException.GetType().ToString(), fhirException.Message);

                    throw;
                }
            }
        }

        private async Task<UpsertOutcome> InternalUpsertAsync(
            ResourceWrapper resource,
            WeakETag weakETag,
            bool allowCreate,
            bool keepHistory,
            CancellationToken cancellationToken,
            bool requireETagOnUpdate = false)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var cosmosWrapper = new FhirCosmosResourceWrapper(resource);
            UpdateSortIndex(cosmosWrapper);

            if (cosmosWrapper.SearchIndices == null || cosmosWrapper.SearchIndices.Count == 0)
            {
                throw new MissingSearchIndicesException(string.Format(Core.Resources.MissingSearchIndices, resource.ResourceTypeName));
            }

            var partitionKey = new PartitionKey(cosmosWrapper.PartitionKey);
            AsyncPolicy retryPolicy = _retryExceptionPolicyFactory.RetryPolicy;

            _logger.LogInformation("Upserting {ResourceType}/{ResourceId}, ETag: \"{Tag}\", AllowCreate: {AllowCreate}, KeepHistory: {KeepHistory}", resource.ResourceTypeName, resource.ResourceId, weakETag?.VersionId, allowCreate, keepHistory);

            if (weakETag == null && allowCreate && !cosmosWrapper.IsDeleted)
            {
                // Optimistically try to create this as a new resource
                try
                {
                    await retryPolicy.ExecuteAsync(
                        async ct => await _containerScope.Value.CreateItemAsync(
                            cosmosWrapper,
                            partitionKey,
                            cancellationToken: ct,
                            requestOptions: new ItemRequestOptions { EnableContentResponseOnWrite = false }),
                        cancellationToken);

                    return new UpsertOutcome(cosmosWrapper, SaveOutcomeType.Created);
                }
                catch (CosmosException e) when (e.StatusCode == HttpStatusCode.Conflict)
                {
                    // this means there is already an existing version of this resource
                }
                catch (CosmosException e) when (e.IsServiceUnavailableDueToTimeout())
                {
                    throw new CosmosException(e.Message, HttpStatusCode.RequestTimeout, e.SubStatusCode, e.ActivityId, e.RequestCharge);
                }
            }

            // If the versioning policy is set to versioned-update and no if-match header is provided
            if (requireETagOnUpdate && weakETag == null)
            {
                // The backwards compatibility behavior of Stu3 is to return 412 Precondition Failed instead of a 400 Client Error
                if (_modelInfoProvider.Version == FhirSpecification.Stu3)
                {
                    _logger.LogInformation("PreconditionFailed: IfMatchHeaderRequiredForResource");
                    throw new PreconditionFailedException(string.Format(Core.Resources.IfMatchHeaderRequiredForResource, resource.ResourceTypeName));
                }

                _logger.LogInformation("BadRequest: IfMatchHeaderRequiredForResource");
                throw new BadRequestException(string.Format(Core.Resources.IfMatchHeaderRequiredForResource, resource.ResourceTypeName));
            }

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                FhirCosmosResourceWrapper existingItemResource;
                try
                {
                    ItemResponse<FhirCosmosResourceWrapper> existingItem = await retryPolicy.ExecuteAsync(
                        async ct => await _containerScope.Value.ReadItemAsync<FhirCosmosResourceWrapper>(cosmosWrapper.Id, partitionKey, cancellationToken: ct),
                        cancellationToken);
                    existingItemResource = existingItem.Resource;
                }
                catch (CosmosException e) when (e.StatusCode == HttpStatusCode.NotFound)
                {
                    if (cosmosWrapper.IsDeleted)
                    {
                        return null;
                    }

                    if (weakETag != null)
                    {
                        _logger.LogInformation("ResourceNotFound: ResourceNotFoundByIdAndVersion");
                        throw new ResourceNotFoundException(string.Format(Core.Resources.ResourceNotFoundByIdAndVersion, resource.ResourceTypeName, resource.ResourceId, weakETag.VersionId));
                    }

                    if (!allowCreate)
                    {
                        _logger.LogInformation("MethodNotAllowed: ResourceCreationNotAllowed");
                        throw new MethodNotAllowedException(Core.Resources.ResourceCreationNotAllowed);
                    }

                    throw;
                }

                if (weakETag != null && weakETag.VersionId != existingItemResource.Version)
                {
                    // The backwards compatibility behavior of Stu3 is to return 409 Conflict instead of a 412 Precondition Failed
                    if (_modelInfoProvider.Version == FhirSpecification.Stu3)
                    {
                        _logger.LogInformation("ResourceConflict: ResourceVersionConflict");
                        throw new ResourceConflictException(weakETag);
                    }

                    _logger.LogInformation("PreconditionFailed: ResourceVersionConflict");
                    throw new PreconditionFailedException(string.Format(Core.Resources.ResourceVersionConflict, weakETag.VersionId));
                }

                if (existingItemResource.IsDeleted && cosmosWrapper.IsDeleted)
                {
                    return null;
                }

                // If not a delete then check if its an update with no data change
                if (!cosmosWrapper.IsDeleted)
                {
                    // check if the new resource data is same as existing resource data
                    if (string.Equals(RemoveVersionIdAndLastUpdatedFromMeta(existingItemResource), RemoveVersionIdAndLastUpdatedFromMeta(cosmosWrapper), StringComparison.Ordinal))
                    {
                        // Do not store the duplicate data, for a update with no impact - returning existingItemResource as no updates
                        return new UpsertOutcome(existingItemResource, SaveOutcomeType.Updated);
                    }
                }

                cosmosWrapper.Version = int.TryParse(existingItemResource.Version, out int existingVersion) ? (existingVersion + 1).ToString(CultureInfo.InvariantCulture) : Guid.NewGuid().ToString();

                // indicate that the version in the raw resource's meta property does not reflect the actual version.
                cosmosWrapper.RawResource.IsMetaSet = false;

                if (cosmosWrapper.RawResource.Format == FhirResourceFormat.Json)
                {
                    // Update the raw resource based on the new version.
                    // This is a lot faster than re-serializing the POCO.
                    // Unfortunately, we need to allocate a string, but at least it is reused for the HTTP response.

                    // If the format is not XML, IsMetaSet will remain false and we will update the version when the resource is read.

                    using MemoryStream memoryStream = _recyclableMemoryStreamManager.GetStream(tag: nameof(CosmosFhirDataStore));
                    await new RawResourceElement(cosmosWrapper).SerializeToStreamAsUtf8Json(memoryStream);

                    if (memoryStream.Length >= BlobSizeThresholdWarningInBytes)
                    {
                        _logger.LogInformation(
                            "{Origin} - MemoryWatch - Heavy serialization in memory. Stream size: {StreamSize}. Current memory in use: {MemoryInUse}.",
                            nameof(CosmosFhirDataStore),
                            memoryStream.Length,
                            GC.GetTotalMemory(forceFullCollection: false));
                    }

                    memoryStream.Position = 0;
                    using var reader = new StreamReader(memoryStream, Encoding.UTF8);
                    cosmosWrapper.RawResource = new RawResource(await reader.ReadToEndAsync(), FhirResourceFormat.Json, isMetaSet: true);
                }

                if (keepHistory)
                {
                    existingItemResource.IsHistory = true;
                    existingItemResource.ActivePeriodEndDateTime = cosmosWrapper.LastModified;
                    existingItemResource.SearchIndices = null;

                    TransactionalBatchResponse transactionalBatchResponse = await retryPolicy.ExecuteAsync(
                        async ct =>
                            await _containerScope.Value.CreateTransactionalBatch(partitionKey)
                                .ReplaceItem(cosmosWrapper.Id, cosmosWrapper, new TransactionalBatchItemRequestOptions { EnableContentResponseOnWrite = false, IfMatchEtag = existingItemResource.ETag })
                                .CreateItem(existingItemResource, new TransactionalBatchItemRequestOptions { EnableContentResponseOnWrite = false })
                                .ExecuteAsync(cancellationToken: ct),
                        cancellationToken);

                    if (!transactionalBatchResponse.IsSuccessStatusCode)
                    {
                        if (transactionalBatchResponse.StatusCode == HttpStatusCode.PreconditionFailed)
                        {
                            // someone else beat us to it, re-read and try again
                            continue;
                        }

                        throw new InvalidOperationException(transactionalBatchResponse.ErrorMessage);
                    }
                }
                else
                {
                    try
                    {
                        await retryPolicy.ExecuteAsync(
                            async ct => await _containerScope.Value.ReplaceItemAsync(
                                cosmosWrapper,
                                cosmosWrapper.Id,
                                partitionKey,
                                new ItemRequestOptions { EnableContentResponseOnWrite = false, IfMatchEtag = existingItemResource.ETag },
                                cancellationToken: ct),
                            cancellationToken);
                    }
                    catch (CosmosException e) when (e.StatusCode == HttpStatusCode.PreconditionFailed)
                    {
                        // someone else beat us to it, re-read and try again
                        continue;
                    }
                }

                return new UpsertOutcome(cosmosWrapper, SaveOutcomeType.Updated);
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

                (IReadOnlyList<FhirCosmosResourceWrapper> results, _) = await _retryExceptionPolicyFactory.RetryPolicy.ExecuteAsync(() =>
                    ExecuteDocumentQueryAsync<FhirCosmosResourceWrapper>(
                        sqlQuerySpec,
                        new QueryRequestOptions { PartitionKey = new PartitionKey(key.ToPartitionKey()) },
                        cancellationToken: cancellationToken));

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

        public async Task HardDeleteAsync(ResourceKey key, bool keepCurrentVersion, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(key, nameof(key));

            try
            {
                _logger.LogDebug("Obliterating {ResourceType}/{Id}. Keep current version: {KeepCurrentVersion}", key.ResourceType, key.Id, keepCurrentVersion);

                StoredProcedureExecuteResponse<IList<string>> response = await _retryExceptionPolicyFactory.RetryPolicy.ExecuteAsync(
                    async ct => await _hardDelete.Execute(
                        _containerScope.Value.Scripts,
                        key,
                        keepCurrentVersion,
                        ct),
                    cancellationToken);

                _logger.LogDebug("Hard-deleted {Count} documents, which consumed {RU} RUs. The list of hard-deleted documents: {Resources}.", response.Resource.Count, response.RequestCharge, string.Join(", ", response.Resource));
            }
            catch (CosmosException exception)
            {
                if (exception.IsRequestRateExceeded())
                {
                    throw;
                }

                _logger.LogError(exception, "Unhandled Document Client Exception");

                throw;
            }
        }

        public async Task BulkUpdateSearchParameterIndicesAsync(IReadOnlyCollection<ResourceWrapper> resources, CancellationToken cancellationToken)
        {
            // TODO: use batch command to update both hash values and search index values for list updateSearchIndices
            // this is a place holder update until we batch update resources
            foreach (var resource in resources)
            {
                await UpdateSearchParameterIndicesAsync(resource, cancellationToken);
            }
        }

        public async Task<ResourceWrapper> UpdateSearchParameterIndicesAsync(ResourceWrapper resourceWrapper, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resourceWrapper, nameof(resourceWrapper));

            var cosmosWrapper = new FhirCosmosResourceWrapper(resourceWrapper);
            UpdateSortIndex(cosmosWrapper);

            try
            {
                _logger.LogDebug("Replacing {ResourceType}/{Id}/{Version}", resourceWrapper.ResourceTypeName, resourceWrapper.ResourceId, resourceWrapper.Version);

                FhirCosmosResourceWrapper response = await _retryExceptionPolicyFactory.RetryPolicy.ExecuteAsync(
                    async ct => await _replaceSingleResource.Execute(
                        _containerScope.Value.Scripts,
                        cosmosWrapper,
                        resourceWrapper.Version,
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
                        _logger.LogError(string.Format(Core.Resources.ResourceVersionConflict, WeakETag.FromVersionId(resourceWrapper.Version)));
                        throw new PreconditionFailedException(string.Format(Core.Resources.ResourceVersionConflict, WeakETag.FromVersionId(resourceWrapper.Version)));

                    case HttpStatusCode.ServiceUnavailable:
                        _logger.LogError("Failed to reindex resource because the Cosmos service was unavailable.");
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
        /// <param name="searchEnumerationTimeoutOverride">
        ///     If specified, overrides <see cref="CosmosDataStoreConfiguration.SearchEnumerationTimeoutInSeconds"/> </param> as the maximum amount of time to spend enumerating pages from the SDK to get at least <see cref="QueryRequestOptions.MaxItemCount"/> * <see cref="ExecuteDocumentQueryAsyncMinimumFillFactor"/> results.
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The results and possible continuation token</returns>
        internal async Task<(IReadOnlyList<T> results, string continuationToken)> ExecuteDocumentQueryAsync<T>(QueryDefinition sqlQuerySpec, QueryRequestOptions feedOptions, string continuationToken = null, bool mustNotExceedMaxItemCount = true, TimeSpan? searchEnumerationTimeoutOverride = default, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(sqlQuerySpec, nameof(sqlQuerySpec));

            int totalDesiredCount = 0;

            if (feedOptions.MaxItemCount.HasValue)
            {
                totalDesiredCount = feedOptions.MaxItemCount.Value;
            }

            var context = new CosmosQueryContext(sqlQuerySpec, feedOptions, continuationToken);
            ICosmosQuery<T> cosmosQuery = null;
            var startTime = Clock.UtcNow;

            FeedResponse<T> page = await _retryExceptionPolicyFactory.RetryPolicy.ExecuteAsync(() =>
            {
                cosmosQuery = _cosmosQueryFactory.Create<T>(_containerScope.Value, context); // SDK throws if we don't recreate this on retry
                return cosmosQuery.ExecuteNextAsync(cancellationToken);
            });

            if (!cosmosQuery.HasMoreResults || !feedOptions.MaxItemCount.HasValue || page.Count >= feedOptions.MaxItemCount)
            {
                if (page.Count == 0)
                {
                    return (Array.Empty<T>(), page.ContinuationToken);
                }

                var singlePageResults = new List<T>(page.Count);
                singlePageResults.AddRange(page);
                return (singlePageResults, page.ContinuationToken);
            }

            // try to obtain at least half of the requested results

            var results = new List<T>(totalDesiredCount);
            results.AddRange(page);

            TimeSpan timeout = (searchEnumerationTimeoutOverride ?? TimeSpan.FromSeconds(_cosmosDataStoreConfiguration.SearchEnumerationTimeoutInSeconds)) - (Clock.UtcNow - startTime);
            if (timeout <= TimeSpan.Zero)
            {
                return (results, page.ContinuationToken);
            }

            using var timeoutTokenSource = new CancellationTokenSource(timeout);
            using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutTokenSource.Token);

            bool executingWithMaxParallelism = feedOptions.MaxConcurrency == _cosmosDataStoreConfiguration.ParallelQueryOptions.MaxQueryConcurrency && continuationToken == null;

            if (executingWithMaxParallelism)
            {
                _logger.LogInformation("Executing {MaxConcurrency} parallel queries across physical partitions", feedOptions.MaxConcurrency);
            }

            var maxCount = executingWithMaxParallelism
                ? totalDesiredCount * (mustNotExceedMaxItemCount ? 1 : ExecuteDocumentQueryAsyncMaximumFillFactor) // in this mode, the SDK likely has already fetched pages, so we might as well consume them
                : totalDesiredCount * ExecuteDocumentQueryAsyncMinimumFillFactor;

            while (cosmosQuery.HasMoreResults &&
                   (results.Count < maxCount)) // we still want to get more results
            {
                // The FHIR spec says we cannot return more items in a bundle than the _count parameter, if specified.
                // If not specified, mustNotExceedMaxItemCount will be false, and we can allow ourselves to go over the limit.
                // The advantage is that we don't need to construct a new query with a new page size.

                int currentDesiredCount = totalDesiredCount - results.Count;
                if (mustNotExceedMaxItemCount && currentDesiredCount != feedOptions.MaxItemCount && !executingWithMaxParallelism)
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
                    var prevPage = page;
                    page = await cosmosQuery.ExecuteNextAsync(linkedTokenSource.Token);

                    if (mustNotExceedMaxItemCount && (page.Count + results.Count > totalDesiredCount))
                    {
                        // page.Count + results.Count should only be larger than our totalDesired
                        // when executingWithMaxParallelism because we have left the feedOption.MaxItemCount
                        // large in order to preserve the pre-fetched resources in a parallel query
                        // This may result in one or more pages of results being thrown away
                        // and a result that is smaller than the totalDesiredCount
                        // TODO: analyze the current result count to determine if is is too few
                        // and possibly start a new query to fill in more results

                        _logger.LogInformation("Returning results with fewer than desired total, {Count} out of {TotalDesired}.", results.Count, totalDesiredCount);
                        return (results, prevPage.ContinuationToken);
                    }
                    else
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

        // This method should only be called by async jobs as some queries could take a long time to traverse all pages.
        internal async Task<IReadOnlyList<T>> ExecutePagedQueryAsync<T>(QueryDefinition sqlQuerySpec, QueryRequestOptions feedOptions, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(sqlQuerySpec, nameof(sqlQuerySpec));

            AsyncPolicy retryPolicy = _retryExceptionPolicyFactory.RetryPolicy;
            var results = new List<T>();

            return await retryPolicy.ExecuteAsync(async () =>
                {
                    using (FeedIterator<T> itr = _containerScope.Value.GetItemQueryIterator<T>(sqlQuerySpec, requestOptions: feedOptions))
                    {
                        while (itr.HasMoreResults)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            FeedResponse<T> response = await itr.ReadNextAsync(cancellationToken);
                            results.AddRange(response.ToList());
                        }

                        return results;
                    }
                });
        }

        private void UpdateSortIndex(FhirCosmosResourceWrapper cosmosWrapper)
        {
            List<SearchParameterInfo> searchParameters = _supportedSearchParameters.Value.GetSearchParameters(cosmosWrapper.ResourceTypeName).Where(x => x.SortStatus != SortParameterStatus.Disabled).ToList();

            if (searchParameters.Any())
            {
                foreach (SearchParameterInfo field in searchParameters)
                {
                    if (cosmosWrapper.SortValues.All(x => x.Value.SearchParameterUri != field.Url))
                    {
                        // Ensure sort property exists
                        cosmosWrapper.SortValues.Add(field.Code, new SortValue(field.Url));
                    }
                }
            }
            else
            {
                cosmosWrapper.SortValues?.Clear();
            }
        }

        private static string RemoveTrailingZerosFromMillisecondsForAGivenDate(DateTimeOffset date)
        {
            // 0000000+ -> +, 0010000+ -> 001+, 0100000+ -> 01+, 0180000+ -> 018+, 1000000 -> 1+, 1100000+ -> 11+, 1010000+ -> 101+
            // ToString("o") - Formats to 2022-03-09T01:40:52.0690000+02:00 but serialized value to string in dB is 2022-03-09T01:40:52.069+02:00
            var formattedDate = date.ToString("o", CultureInfo.InvariantCulture);
            var milliseconds = formattedDate.Substring(20, 7); // get 0690000
            var trimmedMilliseconds = milliseconds.TrimEnd('0'); // get 069
            if (milliseconds.Equals("0000000", StringComparison.Ordinal))
            {
                // when date = 2022-03-09T01:40:52.0000000+02:00, value in dB is 2022-03-09T01:40:52+02:00, we need to replace the . after second
                return formattedDate.Replace("." + milliseconds, string.Empty, StringComparison.Ordinal);
            }

            return formattedDate.Replace(milliseconds, trimmedMilliseconds, StringComparison.Ordinal);
        }

        private static string RemoveVersionIdAndLastUpdatedFromMeta(FhirCosmosResourceWrapper resourceWrapper)
        {
            var versionToReplace = resourceWrapper.RawResource.IsMetaSet ? resourceWrapper.Version : "1";
            var rawResource = resourceWrapper.RawResource.Data.Replace($"\"versionId\":\"{versionToReplace}\"", string.Empty, StringComparison.Ordinal);
            return rawResource.Replace($"\"lastUpdated\":\"{RemoveTrailingZerosFromMillisecondsForAGivenDate(resourceWrapper.LastModified)}\"", string.Empty, StringComparison.Ordinal);
        }

        public void Build(ICapabilityStatementBuilder builder)
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            _logger.LogInformation("CosmosFhirDataStore. Building Capability Statement.");

            Stopwatch watch = Stopwatch.StartNew();
            try
            {
                builder = builder.PopulateDefaultResourceInteractions();
                _logger.LogInformation("CosmosFhirDataStore. 'Default Resource Interactions' built. Elapsed: {ElapsedTime}. Memory: {MemoryInUse}.", watch.Elapsed, GC.GetTotalMemory(forceFullCollection: false));
            }
            catch (Exception e)
            {
                _logger.LogError(e, "CosmosFhirDataStore. 'Default Resource Interactions' failed. Elapsed: {ElapsedTime}. Memory: {MemoryInUse}.", watch.Elapsed, GC.GetTotalMemory(forceFullCollection: false));
                throw;
            }

            try
            {
                watch = Stopwatch.StartNew();
                builder = builder.SyncSearchParametersAsync();
                _logger.LogInformation("CosmosFhirDataStore. 'Search Parameters' built. Elapsed: {ElapsedTime}. Memory: {MemoryInUse}.", watch.Elapsed, GC.GetTotalMemory(forceFullCollection: false));
            }
            catch (Exception e)
            {
                _logger.LogError(e, "CosmosFhirDataStore. 'Search Parameters' failed. Elapsed: {ElapsedTime}. Memory: {MemoryInUse}.", watch.Elapsed, GC.GetTotalMemory(forceFullCollection: false));
                throw;
            }

            try
            {
                watch = Stopwatch.StartNew();
                builder = builder.AddGlobalSearchParameters();
                _logger.LogInformation("CosmosFhirDataStore. 'Global Search Parameters' built. Elapsed: {ElapsedTime}. Memory: {MemoryInUse}.", watch.Elapsed, GC.GetTotalMemory(forceFullCollection: false));
            }
            catch (Exception e)
            {
                _logger.LogError(e, "CosmosFhirDataStore. 'Global Search Parameters' failed. Elapsed: {ElapsedTime}. Memory: {MemoryInUse}.", watch.Elapsed, GC.GetTotalMemory(forceFullCollection: false));
                throw;
            }

            try
            {
                watch = Stopwatch.StartNew();
                builder = builder.SyncProfiles();
                _logger.LogInformation("CosmosFhirDataStore. 'Sync Profiles' built. Elapsed: {ElapsedTime}. Memory: {MemoryInUse}.", watch.Elapsed, GC.GetTotalMemory(forceFullCollection: false));
            }
            catch (Exception e)
            {
                _logger.LogError(e, "CosmosFhirDataStore. 'Sync Profiles' failed. Elapsed: {ElapsedTime}. Memory: {MemoryInUse}.", watch.Elapsed, GC.GetTotalMemory(forceFullCollection: false));
                throw;
            }

            if (_coreFeatures.SupportsBatch)
            {
                try
                {
                    watch = Stopwatch.StartNew();
                    builder.AddGlobalInteraction(SystemRestfulInteraction.Batch);
                    _logger.LogInformation("CosmosFhirDataStore. 'Global Interaction' built. Elapsed: {ElapsedTime}. Memory: {MemoryInUse}.", watch.Elapsed, GC.GetTotalMemory(forceFullCollection: false));
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "CosmosFhirDataStore. 'Global Interaction' failed. Elapsed: {ElapsedTime}. Memory: {MemoryInUse}.", watch.Elapsed, GC.GetTotalMemory(forceFullCollection: false));
                    throw;
                }
            }
        }

        public async Task<int?> GetProvisionedDataStoreCapacityAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                return await _containerScope.Value.ReadThroughputAsync(cancellationToken);
            }
            catch (CosmosException ex)
            {
                _logger.LogWarning("Failed to obtain provisioned RU throughput. Error: {Message}", ex.Message);
                return null;
            }
        }
    }
}
