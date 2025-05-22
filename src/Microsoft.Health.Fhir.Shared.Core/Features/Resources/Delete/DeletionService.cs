// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Core.Features.Audit;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Audit;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.Registration;
using Newtonsoft.Json.Linq;
using Polly;
using Polly.Retry;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class DeletionService : IDeletionService
    {
        private readonly IResourceWrapperFactory _resourceWrapperFactory;
        private readonly Lazy<IConformanceProvider> _conformanceProvider;
        private readonly IScopeProvider<IFhirDataStore> _fhirDataStoreFactory;
        private readonly IScopeProvider<ISearchService> _searchServiceFactory;
        private readonly ResourceIdProvider _resourceIdProvider;
        private readonly AsyncRetryPolicy _retryPolicy;
        private readonly FhirRequestContextAccessor _contextAccessor;
        private readonly IAuditLogger _auditLogger;
        private readonly CoreFeatureConfiguration _configuration;
        private readonly IFhirRuntimeConfiguration _fhirRuntimeConfiguration;
        private readonly ILogger<DeletionService> _logger;

        internal const string DefaultCallerAgent = "Microsoft.Health.Fhir.Server";
        private const int MaxParallelThreads = 64;

        public DeletionService(
            IResourceWrapperFactory resourceWrapperFactory,
            Lazy<IConformanceProvider> conformanceProvider,
            IScopeProvider<IFhirDataStore> fhirDataStoreFactory,
            IScopeProvider<ISearchService> searchServiceFactory,
            ResourceIdProvider resourceIdProvider,
            FhirRequestContextAccessor contextAccessor,
            IAuditLogger auditLogger,
            IOptions<CoreFeatureConfiguration> configuration,
            IFhirRuntimeConfiguration fhirRuntimeConfiguration,
            ILogger<DeletionService> logger)
        {
            _resourceWrapperFactory = EnsureArg.IsNotNull(resourceWrapperFactory, nameof(resourceWrapperFactory));
            _conformanceProvider = EnsureArg.IsNotNull(conformanceProvider, nameof(conformanceProvider));
            _fhirDataStoreFactory = EnsureArg.IsNotNull(fhirDataStoreFactory, nameof(fhirDataStoreFactory));
            _searchServiceFactory = EnsureArg.IsNotNull(searchServiceFactory, nameof(searchServiceFactory));
            _resourceIdProvider = EnsureArg.IsNotNull(resourceIdProvider, nameof(resourceIdProvider));
            _contextAccessor = EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));
            _auditLogger = EnsureArg.IsNotNull(auditLogger, nameof(auditLogger));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            _configuration = EnsureArg.IsNotNull(configuration.Value, nameof(configuration));
            _fhirRuntimeConfiguration = EnsureArg.IsNotNull(fhirRuntimeConfiguration, nameof(fhirRuntimeConfiguration));

            _retryPolicy = Policy
                .Handle<RequestRateExceededException>()
                .WaitAndRetryAsync(3, count => TimeSpan.FromSeconds(Math.Pow(2, count) + RandomNumberGenerator.GetInt32(0, 5)));
        }

        public async Task<ResourceKey> DeleteAsync(DeleteResourceRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            ResourceKey key = request.ResourceKey;

            if (!string.IsNullOrEmpty(key.VersionId))
            {
                throw new MethodNotAllowedException(Core.Resources.DeleteVersionNotAllowed);
            }

            string version = null;

            using var fhirDataStore = _fhirDataStoreFactory.Invoke();
            switch (request.DeleteOperation)
            {
                case DeleteOperation.SoftDelete:
                    ResourceWrapper deletedWrapper = CreateSoftDeletedWrapper(key.ResourceType, request.ResourceKey.Id);

                    bool keepHistory = await _conformanceProvider.Value.CanKeepHistory(key.ResourceType, cancellationToken);

                    UpsertOutcome result = await _retryPolicy.ExecuteAsync(async () => await fhirDataStore.Value.UpsertAsync(new ResourceWrapperOperation(deletedWrapper, true, keepHistory, null, false, false, bundleResourceContext: request.BundleResourceContext), cancellationToken));

                    version = result?.Wrapper.Version;
                    break;
                case DeleteOperation.HardDelete:
                case DeleteOperation.PurgeHistory:
                    await _retryPolicy.ExecuteAsync(async () => await fhirDataStore.Value.HardDeleteAsync(key, request.DeleteOperation == DeleteOperation.PurgeHistory, request.AllowPartialSuccess, cancellationToken));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(request));
            }

            return new ResourceKey(key.ResourceType, key.Id, version);
        }

        public async Task<List<ResourceWrapper>> DeleteMultipleAsync(ConditionalDeleteResourceRequest request, CancellationToken cancellationToken)
        {
            return await DeleteMultipleAsyncInternal(request, MaxParallelThreads, null, cancellationToken);
        }

        private async Task<List<ResourceWrapper>> DeleteMultipleAsyncInternal(ConditionalDeleteResourceRequest request, int parallelThreads, string continuationToken, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            var searchCount = 1000;
            bool tooManyIncludeResults = false;

            IReadOnlyCollection<SearchResultEntry> results;
            string ct;
            string ict;
            using (var searchService = _searchServiceFactory.Invoke())
            {
                (results, ct, ict) = await searchService.Value.ConditionalSearchAsync(
                    request.ResourceType,
                    request.ConditionalParameters,
                    cancellationToken,
                    request.DeleteAll ? searchCount : request.MaxDeleteCount,
                    continuationToken,
                    versionType: request.VersionType,
                    onlyIds: !string.Equals(request.ResourceType, KnownResourceTypes.SearchParameter, StringComparison.OrdinalIgnoreCase),
                    isIncludesOperation: request.IsIncludesRequest,
                    logger: _logger);
            }

            var deletedResources = new List<ResourceWrapper>();

            // If there is more than one page of results included then delete all included results for the first page of resources.
            if (!request.IsIncludesRequest && AreIncludeResultsTruncated())
            {
                try
                {
                    if (!request.DeleteAll || !IsIncludeEnabled())
                    {
                        var innerException = new BadRequestException(string.Format(CultureInfo.InvariantCulture, Core.Resources.TooManyIncludeResults, _configuration.DefaultIncludeCountPerSearch, _configuration.MaxIncludeCountPerSearch));
                        throw new IncompleteOperationException<List<ResourceWrapper>>(innerException, new List<ResourceWrapper>());
                    }

                    ConditionalDeleteResourceRequest clonedRequest = request.Clone();
                    clonedRequest.IsIncludesRequest = true;
                    var cloneList = new List<Tuple<string, string>>(clonedRequest.ConditionalParameters)
                    {
                        Tuple.Create(KnownQueryParameterNames.ContinuationToken, ct),
                    };
                    clonedRequest.ConditionalParameters = cloneList;
                    var subresult = await DeleteMultipleAsyncInternal(clonedRequest, parallelThreads, ict, cancellationToken);

                    if (subresult != null)
                    {
                        deletedResources = new List<ResourceWrapper>(subresult);
                    }
                }
                catch (IncompleteOperationException<List<ResourceWrapper>> ex)
                {
                    _logger.LogError(ex, "Error with include delete");
                    throw new IncompleteOperationException<List<ResourceWrapper>>(ex, ex.PartialResults);
                }
            }

            // Delete the matched results...
            using var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            long numQueuedForDeletion = 0;
            var deleteTasks = new List<Task<List<ResourceWrapper>>>();
            try
            {
                while (results.Any() || !string.IsNullOrEmpty(ct))
                {
                    numQueuedForDeletion += results.Where(result => result.SearchEntryMode == ValueSets.SearchEntryMode.Match).Count();

                    if (request.DeleteOperation == DeleteOperation.SoftDelete)
                    {
                        deleteTasks.Add(SoftDeleteResourcePage(request, results, cancellationTokenSource.Token));
                    }
                    else
                    {
                        deleteTasks.Add(HardDeleteResourcePage(request, results, cancellationTokenSource.Token));
                    }

                    if (deleteTasks.Any((task) => task.IsFaulted || task.IsCanceled))
                    {
                        break;
                    }

                    deletedResources.AddRange(deleteTasks.Where(x => x.IsCompletedSuccessfully).SelectMany(task => task.Result));
                    deleteTasks = deleteTasks.Where(task => !task.IsCompletedSuccessfully).ToList();

                    if (deleteTasks.Count >= parallelThreads)
                    {
                        await deleteTasks[0];
                    }

                    if (!string.IsNullOrEmpty(ct) && (request.DeleteAll || (int)(request.MaxDeleteCount - numQueuedForDeletion) > 0))
                    {
                        using (var searchService = _searchServiceFactory.Invoke())
                        {
                            (results, ct, ict) = await searchService.Value.ConditionalSearchAsync(
                                request.ResourceType,
                                request.ConditionalParameters,
                                cancellationToken,
                                request.DeleteAll ? searchCount : (int)(request.MaxDeleteCount - numQueuedForDeletion),
                                ct,
                                request.VersionType,
                                onlyIds: true,
                                isIncludesOperation: request.IsIncludesRequest,
                                logger: _logger);
                        }

                        // If the next page of results has more than one page of included results, delete all pages of included results before deleting the primary results.
                        if (!request.IsIncludesRequest && AreIncludeResultsTruncated())
                        {
                            try
                            {
                                if (!request.DeleteAll || !IsIncludeEnabled())
                                {
                                    tooManyIncludeResults = true;
                                    break;
                                }

                                ConditionalDeleteResourceRequest clonedRequest = request.Clone();
                                clonedRequest.IsIncludesRequest = true;
                                var cloneList = new List<Tuple<string, string>>(clonedRequest.ConditionalParameters)
                                {
                                    Tuple.Create(KnownQueryParameterNames.ContinuationToken, ct),
                                };
                                clonedRequest.ConditionalParameters = cloneList;
                                var subresult = await DeleteMultipleAsyncInternal(clonedRequest, parallelThreads - deleteTasks.Count, ict, cancellationToken);

                                deletedResources.AddRange(subresult);
                            }
                            catch (IncompleteOperationException<List<ResourceWrapper>> ex)
                            {
                                deletedResources.AddRange(ex.PartialResults);
                                _logger.LogError(ex, "Error with include delete");
                                throw;
                            }
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting");
                await cancellationTokenSource.CancelAsync();
            }

            try
            {
                // We need to wait until all running tasks are cancelled to get a count of resources deleted.
                await Task.WhenAll(deleteTasks);
            }
            catch (AggregateException age) when (age.InnerExceptions.Any(e => e is not TaskCanceledException))
            {
                // If one of the tasks fails, the rest may throw a cancellation exception. Filtering those out as they are noise.
                foreach (var coreException in age.InnerExceptions.Where(e => e is not TaskCanceledException))
                {
                    _logger.LogError(coreException, "Error deleting");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting");
            }

            deletedResources.AddRange(deleteTasks.Where(x => x.IsCompletedSuccessfully).SelectMany(task => task.Result));

            if (deleteTasks.Any((task) => task.IsFaulted || task.IsCanceled) || tooManyIncludeResults)
            {
                var exceptions = new List<Exception>();
                if (tooManyIncludeResults)
                {
                    exceptions.Add(
                        new BadRequestException(
                            string.Format(CultureInfo.InvariantCulture, Core.Resources.TooManyIncludeResults, _configuration.DefaultIncludeCountPerSearch, _configuration.MaxIncludeCountPerSearch)));
                }

                deleteTasks.Where((task) => task.IsFaulted || task.IsCanceled).ToList().ForEach(x =>
                {
                    if (x.Exception != null)
                    {
                        // Count the number of resources deleted before the exception was thrown. Update the total.
                        if (x.Exception.InnerExceptions.Any(ex => ex is IncompleteOperationException<List<ResourceWrapper>>))
                        {
                            var partialResults = x.Exception.InnerExceptions
                                .Where((ex) => ex is IncompleteOperationException<List<ResourceWrapper>>)
                                .SelectMany(ex => ((IncompleteOperationException<List<ResourceWrapper>>)ex).PartialResults);
                            deletedResources.AddRange(partialResults);
                        }

                        if (x.IsFaulted)
                        {
                            // Filter out noise from the cancellation exceptions caused by the core exception.
                            exceptions.AddRange(x.Exception.InnerExceptions.Where(e => e is not TaskCanceledException));
                        }
                    }
                });

                var aggregateException = new AggregateException(exceptions);
                throw new IncompleteOperationException<List<ResourceWrapper>>(
                    aggregateException,
                    deletedResources);
            }

            return deletedResources;
        }

        private async Task<List<ResourceWrapper>> SoftDeleteResourcePage(ConditionalDeleteResourceRequest request, IReadOnlyCollection<SearchResultEntry> resourcesToDelete, CancellationToken cancellationToken)
        {
            await CreateAuditLog(
                request.ResourceType,
                request.DeleteOperation,
                false,
                resourcesToDelete.Select((item) => (item.Resource.ResourceTypeName, item.Resource.ResourceId, item.SearchEntryMode == ValueSets.SearchEntryMode.Include)));

            ResourceWrapperOperation[] softDeleteIncludes = await Task.WhenAll(resourcesToDelete.Where(resource => resource.SearchEntryMode == ValueSets.SearchEntryMode.Include).Select(async item =>
            {
                // If there isn't a cached capability statement (IE this is the first request made after a service starts up) then performance on this request will be terrible as the capability statement needs to be rebuilt for every resource.
                // This is because the capability statement can't be made correctly in a background job, so it doesn't cache the result.
                // The result is good enough for background work, but can't be used for metadata as the urls aren't formated properly.
                bool keepHistory = await _conformanceProvider.Value.CanKeepHistory(item.Resource.ResourceTypeName, cancellationToken);
                ResourceWrapper deletedWrapper = CreateSoftDeletedWrapper(item.Resource.ResourceTypeName, item.Resource.ResourceId);
                return new ResourceWrapperOperation(deletedWrapper, true, keepHistory, null, false, false, bundleResourceContext: request.BundleResourceContext);
            }));

            ResourceWrapperOperation[] softDeleteMatches = await Task.WhenAll(resourcesToDelete.Where(resource => resource.SearchEntryMode == ValueSets.SearchEntryMode.Match).Select(async item =>
            {
                bool keepHistory = await _conformanceProvider.Value.CanKeepHistory(item.Resource.ResourceTypeName, cancellationToken);
                ResourceWrapper deletedWrapper = CreateSoftDeletedWrapper(item.Resource.ResourceTypeName, item.Resource.ResourceId);
                return new ResourceWrapperOperation(deletedWrapper, true, keepHistory, null, false, false, bundleResourceContext: request.BundleResourceContext);
            }));

            var deletedResources = new List<ResourceWrapper>();
            try
            {
                using var fhirDataStore = _fhirDataStoreFactory.Invoke();

                // Delete includes first so that if there is a failure, the match resources are not deleted. This allows the job to restart.
                if (softDeleteIncludes.Any())
                {
                    await fhirDataStore.Value.MergeAsync(softDeleteIncludes, cancellationToken);
                    deletedResources.AddRange(softDeleteIncludes.Select(x => x.Wrapper));
                }

                await fhirDataStore.Value.MergeAsync(softDeleteMatches, cancellationToken);
            }
            catch (IncompleteOperationException<IDictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>> ex)
            {
                _logger.LogError(ex.InnerException, "Error soft deleting");

                var partialResultIds = ex.PartialResults.Select(x => x.Key).ToDictionary(x => x.Id, x => x);
                var ids = resourcesToDelete.Where(x => partialResultIds.TryGetValue(x.Resource.ResourceId, out _))
                    .Select(x => (x.Resource.ResourceTypeName, x.Resource.ResourceId, x.SearchEntryMode == ValueSets.SearchEntryMode.Include)).ToList();
                ids.AddRange(deletedResources.Select(x => (x.ResourceTypeName, x.ResourceId, true)));
                await CreateAuditLog(request.ResourceType, request.DeleteOperation, true, ids);

                var partialResults = resourcesToDelete.Where(x => partialResultIds.TryGetValue(x.Resource.ResourceId, out _))
                    .Select(x => x.Resource).ToList();
                throw new IncompleteOperationException<List<ResourceWrapper>>(
                    ex.InnerException,
                    partialResults);
            }

            await CreateAuditLog(
                request.ResourceType,
                request.DeleteOperation,
                true,
                resourcesToDelete.Select((item) => (item.Resource.ResourceTypeName, item.Resource.ResourceId, item.SearchEntryMode == ValueSets.SearchEntryMode.Include)));

            return resourcesToDelete.Select(x => x.Resource).ToList();
        }

        private async Task<List<ResourceWrapper>> HardDeleteResourcePage(ConditionalDeleteResourceRequest request, IReadOnlyCollection<SearchResultEntry> resourcesToDelete, CancellationToken cancellationToken)
        {
            await CreateAuditLog(
                request.ResourceType,
                request.DeleteOperation,
                false,
                resourcesToDelete.Select((item) => (item.Resource.ResourceTypeName, item.Resource.ResourceId, item.SearchEntryMode == ValueSets.SearchEntryMode.Include)));

            var parallelBag = new ConcurrentBag<(ResourceWrapper, bool)>();
            try
            {
                using var fhirDataStore = _fhirDataStoreFactory.Invoke();

                var includedResources = resourcesToDelete.Where(resource => resource.SearchEntryMode == ValueSets.SearchEntryMode.Include).ToList();
                var matchedResources = resourcesToDelete.Where(resource => resource.SearchEntryMode == ValueSets.SearchEntryMode.Match).ToList();

                // Delete includes first so that if there is a failure, the match resources are not deleted. This allows the job to restart.
                // This throws AggrigateExceptions
                await Parallel.ForEachAsync(includedResources, cancellationToken, async (item, innerCt) =>
                {
                    await _retryPolicy.ExecuteAsync(async () => await fhirDataStore.Value.HardDeleteAsync(new ResourceKey(item.Resource.ResourceTypeName, item.Resource.ResourceId), request.DeleteOperation == DeleteOperation.PurgeHistory, request.AllowPartialSuccess, innerCt));
                    parallelBag.Add((item.Resource, true));
                });

                await Parallel.ForEachAsync(matchedResources, cancellationToken, async (item, innerCt) =>
                {
                    await _retryPolicy.ExecuteAsync(async () => await fhirDataStore.Value.HardDeleteAsync(new ResourceKey(item.Resource.ResourceTypeName, item.Resource.ResourceId), request.DeleteOperation == DeleteOperation.PurgeHistory, request.AllowPartialSuccess, innerCt));
                    parallelBag.Add((item.Resource, false));
                });
            }
            catch (Exception ex)
            {
                await CreateAuditLog(request.ResourceType, request.DeleteOperation, true, parallelBag.Select(x => (x.Item1.ResourceTypeName, x.Item1.ResourceId, x.Item2)));
                throw new IncompleteOperationException<List<ResourceWrapper>>(ex, parallelBag.Select(x => x.Item1).ToList());
            }

            await CreateAuditLog(request.ResourceType, request.DeleteOperation, true, parallelBag.Select(x => (x.Item1.ResourceTypeName, x.Item1.ResourceId, x.Item2)));

            return parallelBag.Select(x => x.Item1).ToList();
        }

        private ResourceWrapper CreateSoftDeletedWrapper(string resourceType, string resourceId)
        {
            EnsureArg.IsNotNullOrEmpty(resourceType, nameof(resourceType));
            EnsureArg.IsNotNullOrEmpty(resourceId, nameof(resourceId));

            ISourceNode emptySourceNode = FhirJsonNode.Create(
                JObject.FromObject(
                    new
                    {
                        resourceType,
                        id = resourceId,
                    }));

            ResourceWrapper deletedWrapper = _resourceWrapperFactory.CreateResourceWrapper(emptySourceNode.ToPoco<Resource>(), _resourceIdProvider, deleted: true, keepMeta: false);
            return deletedWrapper;
        }

        private System.Threading.Tasks.Task CreateAuditLog(string primaryResourceType, DeleteOperation operation, bool complete, IEnumerable<(string resourceType, string resourceId, bool included)> items, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            var auditTask = System.Threading.Tasks.Task.Run(() =>
            {
                AuditAction action = complete ? AuditAction.Executed : AuditAction.Executing;
                var context = _contextAccessor.RequestContext;
                var deleteAdditionalProperties = new Dictionary<string, string>();
                deleteAdditionalProperties["Affected Items"] = items.Aggregate(
                    string.Empty,
                    (aggregate, item) =>
                    {
                        aggregate += ", " + (item.included ? "[Include] " : string.Empty) + item.resourceType + "/" + item.resourceId;
                        return aggregate;
                    });

                _auditLogger.LogAudit(
                    auditAction: action,
                    operation: operation.ToString(),
                    resourceType: primaryResourceType,
                    requestUri: context.Uri,
                    statusCode: statusCode,
                    correlationId: context.CorrelationId,
                    callerIpAddress: string.Empty,
                    callerClaims: null,
                    customHeaders: null,
                    operationType: string.Empty,
                    callerAgent: DefaultCallerAgent,
                    additionalProperties: deleteAdditionalProperties);
            });

            return auditTask;
        }

        private bool AreIncludeResultsTruncated()
        {
            return _contextAccessor.RequestContext.BundleIssues.Any(
                x => string.Equals(x.Diagnostics, Core.Resources.TruncatedIncludeMessage, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x.Diagnostics, Core.Resources.TruncatedIncludeMessageForIncludes, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsIncludeEnabled()
        {
            return _configuration.SupportsIncludes && (_fhirRuntimeConfiguration.DataStore?.Equals(KnownDataStores.SqlServer, StringComparison.OrdinalIgnoreCase) ?? false);
        }
    }
}
