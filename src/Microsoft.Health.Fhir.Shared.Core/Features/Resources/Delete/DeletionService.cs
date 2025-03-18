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
using Microsoft.Health.Fhir.Core.Messages.Delete;
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

        public async Task<IDictionary<string, long>> DeleteMultipleAsync(ConditionalDeleteResourceRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            var searchCount = 1000;
            bool tooManyIncludeResults = false;

            IReadOnlyCollection<SearchResultEntry> results;
            string ct;
            using (var searchService = _searchServiceFactory.Invoke())
            {
                (results, ct) = await searchService.Value.ConditionalSearchAsync(
                    request.ResourceType,
                    request.ConditionalParameters,
                    cancellationToken,
                    request.DeleteAll ? searchCount : request.MaxDeleteCount,
                    versionType: request.VersionType,
                    onlyIds: true,
                    logger: _logger);
            }

            Dictionary<string, long> resourceTypesDeleted = new Dictionary<string, long>();
            long numQueuedForDeletion = 0;

            var deleteTasks = new List<Task<Dictionary<string, long>>>();
            using var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            if (AreIncludeResultsTruncated())
            {
                var innerException = new BadRequestException(string.Format(CultureInfo.InvariantCulture, Core.Resources.TooManyIncludeResults, _configuration.DefaultIncludeCountPerSearch, _configuration.MaxIncludeCountPerSearch));
                throw new IncompleteOperationException<Dictionary<string, long>>(innerException, resourceTypesDeleted);
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

                    if (deleteTasks.Count >= MaxParallelThreads)
                    {
                        await deleteTasks[0];
                    }

                    if (!string.IsNullOrEmpty(ct) && (request.DeleteAll || (int)(request.MaxDeleteCount - numQueuedForDeletion) > 0))
                    {
                        using (var searchService = _searchServiceFactory.Invoke())
                        {
                            (results, ct) = await searchService.Value.ConditionalSearchAsync(
                                request.ResourceType,
                                request.ConditionalParameters,
                                cancellationToken,
                                request.DeleteAll ? searchCount : (int)(request.MaxDeleteCount - numQueuedForDeletion),
                                ct,
                                request.VersionType,
                                onlyIds: true,
                                logger: _logger);
                        }

                        if (AreIncludeResultsTruncated())
                        {
                            tooManyIncludeResults = true;
                            break;
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

            ResourceWrapperOperation[] softDeleteMatches = await Task.WhenAll(resourcesToDelete.Where(resource => resource.SearchEntryMode == ValueSets.SearchEntryMode.Match).Select(async item =>
            {
                bool keepHistory = await _conformanceProvider.Value.CanKeepHistory(item.Resource.ResourceTypeName, cancellationToken);
                ResourceWrapper deletedWrapper = CreateSoftDeletedWrapper(item.Resource.ResourceTypeName, item.Resource.ResourceId);
                return new ResourceWrapperOperation(deletedWrapper, true, keepHistory, null, false, false, bundleResourceContext: request.BundleResourceContext);
            }));

            ResourceWrapperOperation[] softDeleteIncludes = await Task.WhenAll(resourcesToDelete.Where(resource => resource.SearchEntryMode == ValueSets.SearchEntryMode.Include).Select(async item =>
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
                using var fhirDataStore = _fhirDataStoreFactory.Invoke();

                var matchedResources = resourcesToDelete.Where(resource => resource.SearchEntryMode == ValueSets.SearchEntryMode.Match).ToList();
                var includedResources = resourcesToDelete.Where(resource => resource.SearchEntryMode == ValueSets.SearchEntryMode.Include).ToList();

                // Delete includes first so that if there is a failure, the match resources are not deleted. This allows the job to restart.
                // This throws AggrigateExceptions
                await Parallel.ForEachAsync(includedResources, cancellationToken, async (item, innerCt) =>
                {
                    await _retryPolicy.ExecuteAsync(async () => await fhirDataStore.Value.HardDeleteAsync(new ResourceKey(item.Resource.ResourceTypeName, item.Resource.ResourceId), request.DeleteOperation == DeleteOperation.PurgeHistory, request.AllowPartialSuccess, innerCt));
                    parallelBag.Add((item.Resource.ResourceTypeName, item.Resource.ResourceId, item.SearchEntryMode == ValueSets.SearchEntryMode.Include));
                });

                await Parallel.ForEachAsync(matchedResources, cancellationToken, async (item, innerCt) =>
                {
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

        private bool AreIncludeResultsTruncated()
        {
            return _contextAccessor.RequestContext.BundleIssues.Any(x => string.Equals(x.Diagnostics, Core.Resources.TruncatedIncludeMessage, StringComparison.OrdinalIgnoreCase));
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
    }
}
