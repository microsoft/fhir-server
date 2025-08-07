// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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
using Microsoft.Build.Framework;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Core.Features.Audit;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Audit;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
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
        private readonly ISearchParameterOperations _searchParameterOperations;
        private readonly IResourceDeserializer _resourceDeserializer;
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
            ISearchParameterOperations searchParameterOperations,
            IResourceDeserializer resourceDeserializer,
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
            _searchParameterOperations = EnsureArg.IsNotNull(searchParameterOperations, nameof(searchParameterOperations));
            _resourceDeserializer = EnsureArg.IsNotNull(resourceDeserializer, nameof(resourceDeserializer));

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

        public async Task<IDictionary<string, long>> DeleteMultipleAsync(ConditionalDeleteResourceRequest request, CancellationToken cancellationToken, IList<string> excludedResourceTypes = null)
        {
            return await DeleteMultipleAsyncInternal(request, MaxParallelThreads, excludedResourceTypes, null, cancellationToken);
        }

        private async Task<IDictionary<string, long>> DeleteMultipleAsyncInternal(ConditionalDeleteResourceRequest request, int parallelThreads, IList<string> excludedResourceTypes, string continuationToken, CancellationToken cancellationToken)
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

            // Filter results to exclude resourceTypes included in excludedResourceTypes
            if (excludedResourceTypes != null && excludedResourceTypes.Count > 0)
            {
                var excludedResourceTypesSet = new HashSet<string>(excludedResourceTypes, StringComparer.OrdinalIgnoreCase);
                results = results
                    .Where(x => !excludedResourceTypesSet.Contains(x.Resource.ResourceTypeName))
                    .ToList();
            }

            Dictionary<string, long> resourceTypesDeleted = new Dictionary<string, long>();
            long numQueuedForDeletion = 0;

            var deleteTasks = new List<Task<Dictionary<string, long>>>();
            using var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // If there is more than one page of results included then delete all included results for the first page of resources.
            if (!request.IsIncludesRequest && AreIncludeResultsTruncated())
            {
                try
                {
                    if (!request.DeleteAll || !IsIncludeEnabled())
                    {
                        var innerException = new BadRequestException(string.Format(CultureInfo.InvariantCulture, Core.Resources.TooManyIncludeResults, _configuration.DefaultIncludeCountPerSearch, _configuration.MaxIncludeCountPerSearch));
                        throw new IncompleteOperationException<Dictionary<string, long>>(innerException, resourceTypesDeleted);
                    }

                    ConditionalDeleteResourceRequest clonedRequest = request.Clone();
                    clonedRequest.IsIncludesRequest = true;
                    var cloneList = new List<Tuple<string, string>>(clonedRequest.ConditionalParameters)
                    {
                        Tuple.Create(KnownQueryParameterNames.ContinuationToken, ct),
                    };
                    clonedRequest.ConditionalParameters = cloneList;
                    var subresult = await DeleteMultipleAsyncInternal(clonedRequest, parallelThreads, excludedResourceTypes, ict, cancellationToken);

                    if (subresult != null)
                    {
                        resourceTypesDeleted = new Dictionary<string, long>(subresult);
                    }
                }
                catch (IncompleteOperationException<Dictionary<string, long>> ex)
                {
                    _logger.LogError(ex, "Error with include delete");
                    throw new IncompleteOperationException<Dictionary<string, long>>(ex, ex.PartialResults);
                }
            }

            // Delete the matched results...
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

                    resourceTypesDeleted = AppendDeleteResults(resourceTypesDeleted, deleteTasks.Where(x => x.IsCompletedSuccessfully).Select(task => task.Result));

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
                                onlyIds: !string.Equals(request.ResourceType, KnownResourceTypes.SearchParameter, StringComparison.OrdinalIgnoreCase),
                                isIncludesOperation: request.IsIncludesRequest,
                                logger: _logger);
                        }

                        // Filter results to exclude resourceTypes included in excludedResourceTypes
                        if (excludedResourceTypes != null && excludedResourceTypes.Count > 0)
                        {
                            var excludedResourceTypesSet = new HashSet<string>(excludedResourceTypes, StringComparer.OrdinalIgnoreCase);
                            results = results
                                .Where(x => !excludedResourceTypesSet.Contains(x.Resource.ResourceTypeName))
                                .ToList();
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
                                var subresult = await DeleteMultipleAsyncInternal(clonedRequest, parallelThreads - deleteTasks.Count, excludedResourceTypes, ict, cancellationToken);

                                resourceTypesDeleted = AppendDeleteResults(resourceTypesDeleted, new List<Dictionary<string, long>>() { new Dictionary<string, long>(subresult) });
                            }
                            catch (IncompleteOperationException<Dictionary<string, long>> ex)
                            {
                                resourceTypesDeleted = AppendDeleteResults(resourceTypesDeleted, new List<Dictionary<string, long>>() { ex.PartialResults });
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

            resourceTypesDeleted = AppendDeleteResults(resourceTypesDeleted, deleteTasks.Where(x => x.IsCompletedSuccessfully).Select(task => task.Result));

            if (deleteTasks.Any((task) => task.IsFaulted || task.IsCanceled) || tooManyIncludeResults)
            {
                var exceptions = new List<Exception>();

                if (tooManyIncludeResults)
                {
                    exceptions.Add(new BadRequestException(string.Format(CultureInfo.InvariantCulture, Core.Resources.TooManyIncludeResults, _configuration.DefaultIncludeCountPerSearch, _configuration.MaxIncludeCountPerSearch)));
                }

                deleteTasks.Where((task) => task.IsFaulted || task.IsCanceled).ToList().ForEach((Task<Dictionary<string, long>> result) =>
                    {
                        if (result.Exception != null)
                        {
                            // Count the number of resources deleted before the exception was thrown. Update the total.
                            if (result.Exception.InnerExceptions.Any(ex => ex is IncompleteOperationException<Dictionary<string, long>>))
                            {
                                AppendDeleteResults(
                                    resourceTypesDeleted,
                                    result.Exception.InnerExceptions.Where((ex) => ex is IncompleteOperationException<Dictionary<string, long>>)
                                        .Select(ex => ((IncompleteOperationException<Dictionary<string, long>>)ex).PartialResults));
                            }

                            if (result.IsFaulted)
                            {
                                // Filter out noise from the cancellation exceptions caused by the core exception.
                                exceptions.AddRange(result.Exception.InnerExceptions.Where(e => e is not TaskCanceledException));
                            }
                        }
                    });
                var aggregateException = new AggregateException(exceptions);
                throw new IncompleteOperationException<Dictionary<string, long>>(aggregateException, resourceTypesDeleted);
            }

            return resourceTypesDeleted;
        }

        private async Task<Dictionary<string, long>> SoftDeleteResourcePage(ConditionalDeleteResourceRequest request, IReadOnlyCollection<SearchResultEntry> resourcesToDelete, CancellationToken cancellationToken)
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

            var partialResults = new List<(string, string, bool)>();
            try
            {
                using var fhirDataStore = _fhirDataStoreFactory.Invoke();

                // Delete includes first so that if there is a failure, the match resources are not deleted. This allows the job to restart.
                if (softDeleteIncludes.Any())
                {
                    await fhirDataStore.Value.MergeAsync(softDeleteIncludes, cancellationToken);
                    partialResults.AddRange(softDeleteIncludes.Select(item => (
                        item.Wrapper.ResourceTypeName,
                        item.Wrapper.ResourceId,
                        resourcesToDelete
                            .Where(resource => resource.Resource.ResourceId == item.Wrapper.ResourceId && resource.Resource.ResourceTypeName == item.Wrapper.ResourceTypeName)
                            .FirstOrDefault().SearchEntryMode == ValueSets.SearchEntryMode.Include)));
                }

                await DeleteSearchParametersAsync(
                    resourcesToDelete.Where(resource => resource.SearchEntryMode == ValueSets.SearchEntryMode.Match).Select(x => x.Resource).ToList(),
                    cancellationToken);

                await fhirDataStore.Value.MergeAsync(softDeleteMatches, cancellationToken);
            }
            catch (IncompleteOperationException<IDictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>> ex)
            {
                _logger.LogError(ex.InnerException, "Error soft deleting");

                var ids = ex.PartialResults.Select(item => (
                    item.Key.ResourceType,
                    item.Key.Id,
                    resourcesToDelete
                        .Where(resource => resource.Resource.ResourceId == item.Key.Id && resource.Resource.ResourceTypeName == item.Key.ResourceType)
                        .FirstOrDefault().SearchEntryMode == ValueSets.SearchEntryMode.Include)).ToList();

                ids.AddRange(partialResults);

                await CreateAuditLog(request.ResourceType, request.DeleteOperation, true, ids);

                throw new IncompleteOperationException<Dictionary<string, long>>(
                    ex.InnerException,
                    ids.GroupBy(pair => pair.ResourceType).ToDictionary(group => group.Key, group => (long)group.Count()));
            }

            await CreateAuditLog(
                request.ResourceType,
                request.DeleteOperation,
                true,
                resourcesToDelete.Select((item) => (item.Resource.ResourceTypeName, item.Resource.ResourceId, item.SearchEntryMode == ValueSets.SearchEntryMode.Include)));

            return resourcesToDelete.GroupBy(x => x.Resource.ResourceTypeName).ToDictionary(x => x.Key, x => (long)x.Count());
        }

        private async Task<Dictionary<string, long>> HardDeleteResourcePage(ConditionalDeleteResourceRequest request, IReadOnlyCollection<SearchResultEntry> resourcesToDelete, CancellationToken cancellationToken)
        {
            await CreateAuditLog(
                request.ResourceType,
                request.DeleteOperation,
                false,
                resourcesToDelete.Select((item) => (item.Resource.ResourceTypeName, item.Resource.ResourceId, item.SearchEntryMode == ValueSets.SearchEntryMode.Include)));

            var parallelBag = new ConcurrentBag<(string, string, bool)>();
            try
            {
                if (request.RemoveReferences)
                {
                    foreach (var item in resourcesToDelete)
                    {
                        await RemoveReferences(item, request, cancellationToken);
                    }
                }

                using var fhirDataStore = _fhirDataStoreFactory.Invoke();

                var includedResources = resourcesToDelete.Where(resource => resource.SearchEntryMode == ValueSets.SearchEntryMode.Include).ToList();
                var matchedResources = resourcesToDelete.Where(resource => resource.SearchEntryMode == ValueSets.SearchEntryMode.Match).ToList();

                // Delete includes first so that if there is a failure, the match resources are not deleted. This allows the job to restart.
                // This throws AggrigateExceptions
                await Parallel.ForEachAsync(includedResources, cancellationToken, async (item, innerCt) =>
                {
                    await DeleteSearchParameterAsync(item.Resource, cancellationToken);
                    await _retryPolicy.ExecuteAsync(async () => await fhirDataStore.Value.HardDeleteAsync(new ResourceKey(item.Resource.ResourceTypeName, item.Resource.ResourceId), request.DeleteOperation == DeleteOperation.PurgeHistory, request.AllowPartialSuccess, innerCt));
                    parallelBag.Add((item.Resource.ResourceTypeName, item.Resource.ResourceId, item.SearchEntryMode == ValueSets.SearchEntryMode.Include));
                });

                await Parallel.ForEachAsync(matchedResources, cancellationToken, async (item, innerCt) =>
                {
                    await DeleteSearchParameterAsync(item.Resource, cancellationToken);
                    await _retryPolicy.ExecuteAsync(async () => await fhirDataStore.Value.HardDeleteAsync(new ResourceKey(item.Resource.ResourceTypeName, item.Resource.ResourceId), request.DeleteOperation == DeleteOperation.PurgeHistory, request.AllowPartialSuccess, innerCt));
                    parallelBag.Add((item.Resource.ResourceTypeName, item.Resource.ResourceId, item.SearchEntryMode == ValueSets.SearchEntryMode.Include));
                });
            }
            catch (Exception ex)
            {
                await CreateAuditLog(request.ResourceType, request.DeleteOperation, true, parallelBag);
                throw new IncompleteOperationException<Dictionary<string, long>>(ex, parallelBag.GroupBy(x => x.Item1).ToDictionary(x => x.Key, x => (long)x.Count()));
            }

            await CreateAuditLog(request.ResourceType, request.DeleteOperation, true, parallelBag);

            return parallelBag.GroupBy(x => x.Item1).ToDictionary(x => x.Key, x => (long)x.Count());
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

        private async Task RemoveReferences(SearchResultEntry resource, ConditionalDeleteResourceRequest request, CancellationToken cancellationToken)
        {
            using (var searchService = _searchServiceFactory.Invoke())
            {
                var parameters = new List<Tuple<string, string>>()
                {
                    Tuple.Create(KnownQueryParameterNames.Id, resource.Resource.ResourceId),
                    Tuple.Create(KnownQueryParameterNames.ReverseInclude, "*:*"),
                };
                var results = await searchService.Value.SearchAsync(
                    resourceType: resource.Resource.ResourceTypeName,
                    queryParameters: parameters,
                    cancellationToken: cancellationToken);
                var includesContinuationToken = results.IncludesContinuationToken;
                var revincludeResults = results.Results.Where(result => result.Resource.ResourceId != resource.Resource.ResourceId);

                while (revincludeResults.Any())
                {
                    var resourcesWithReferences = revincludeResults.Select(entry => _resourceDeserializer.Deserialize(entry.Resource));

                    var modifiedResources = new List<ResourceWrapperOperation>();
                    foreach (var reference in resourcesWithReferences)
                    {
                        ReferenceRemover.RemoveReference(reference.ToPoco(), resource.Resource.ResourceTypeName + "/" + resource.Resource.ResourceId);
                        var wrapper = _resourceWrapperFactory.Create(reference, deleted: false, keepMeta: false);
                        modifiedResources.Add(new ResourceWrapperOperation(
                            wrapper,
                            allowCreate: false,
                            keepHistory: true,
                            weakETag: null,
                            requireETagOnUpdate: false,
                            keepVersion: false,
                            bundleResourceContext: request.BundleResourceContext));
                    }

                    using var fhirDataStore = _fhirDataStoreFactory.Invoke();

                    await fhirDataStore.Value.MergeAsync(modifiedResources, cancellationToken);

                    if (includesContinuationToken != null)
                    {
                        var clonedParameters = new List<Tuple<string, string>>(parameters);
                        clonedParameters.Add(Tuple.Create(KnownQueryParameterNames.IncludesContinuationToken, ContinuationTokenEncoder.Encode(includesContinuationToken)));
                        results = await searchService.Value.SearchAsync(
                            resourceType: resource.Resource.ResourceTypeName,
                            queryParameters: clonedParameters,
                            cancellationToken: cancellationToken,
                            isIncludesOperation: true);
                        includesContinuationToken = results.IncludesContinuationToken;
                        revincludeResults = results.Results;
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }

        private bool AreIncludeResultsTruncated()
        {
            return _contextAccessor.RequestContext.BundleIssues.Any(
                x => string.Equals(x.Diagnostics, Core.Resources.TruncatedIncludeMessage, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x.Diagnostics, Core.Resources.TruncatedIncludeMessageForIncludes, StringComparison.OrdinalIgnoreCase));
        }

        private static Dictionary<string, long> AppendDeleteResults(Dictionary<string, long> results, IEnumerable<Dictionary<string, long>> newResults)
        {
            foreach (var newResult in newResults)
            {
                foreach (var (key, value) in newResult)
                {
                    if (!results.TryAdd(key, value))
                    {
                        results[key] += value;
                    }
                }
            }

            return results;
        }

        private bool IsIncludeEnabled()
        {
            return _configuration.SupportsIncludes && (_fhirRuntimeConfiguration.DataStore?.Equals(KnownDataStores.SqlServer, StringComparison.OrdinalIgnoreCase) ?? false);
        }

        private async Task DeleteSearchParametersAsync(IEnumerable<ResourceWrapper> resources, CancellationToken cancellationToken)
        {
            if (resources?.Any() ?? false)
            {
                foreach (var resource in resources.Where(x => string.Equals(x?.ResourceTypeName, KnownResourceTypes.SearchParameter, StringComparison.OrdinalIgnoreCase)))
                {
                    await _searchParameterOperations.DeleteSearchParameterAsync(resource.RawResource, cancellationToken, true);
                }
            }
        }

        private async Task DeleteSearchParameterAsync(ResourceWrapper resource, CancellationToken cancellationToken)
        {
            if (string.Equals(resource?.ResourceTypeName, KnownResourceTypes.SearchParameter, StringComparison.OrdinalIgnoreCase))
            {
                await _searchParameterOperations.DeleteSearchParameterAsync(resource.RawResource, cancellationToken, true);
            }
        }
    }
}
