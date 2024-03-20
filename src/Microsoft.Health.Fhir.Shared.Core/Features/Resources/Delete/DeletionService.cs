// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Core.Features.Audit;
using Microsoft.Health.Extensions.DependencyInjection;
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
                    await _retryPolicy.ExecuteAsync(async () => await fhirDataStore.Value.HardDeleteAsync(key, request.DeleteOperation == DeleteOperation.PurgeHistory, cancellationToken));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(request));
            }

            return new ResourceKey(key.ResourceType, key.Id, version);
        }

        public async Task<long> DeleteMultipleAsync(ConditionalDeleteResourceRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            var searchCount = 1000;

            IReadOnlyCollection<SearchResultEntry> matchedResults;
            string ct;
            using (var searchService = _searchServiceFactory.Invoke())
            {
                (matchedResults, ct) = await searchService.Value.ConditionalSearchAsync(
                    request.ResourceType,
                    request.ConditionalParameters,
                    cancellationToken,
                    request.DeleteAll ? searchCount : request.MaxDeleteCount,
                    versionType: request.VersionType,
                    onlyIds: true);
            }

            long numDeleted = 0;
            long numQueuedForDeletion = 0;

            var deleteTasks = new List<Task<long>>();
            using var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // Delete the matched results...
            try
            {
                while (matchedResults.Any() || !string.IsNullOrEmpty(ct))
                {
                    IReadOnlyCollection<SearchResultEntry> resultsToDelete =
                        request.DeleteAll ? matchedResults : matchedResults.Take(Math.Max((int)(request.MaxDeleteCount.GetValueOrDefault() - numQueuedForDeletion), 0)).ToArray();

                    numQueuedForDeletion += resultsToDelete.Count;

                    if (request.DeleteOperation == DeleteOperation.SoftDelete)
                    {
                        deleteTasks.Add(SoftDeleteResourcePage(request, resultsToDelete, cancellationTokenSource.Token));
                    }
                    else
                    {
                        deleteTasks.Add(HardDeleteResourcePage(request, resultsToDelete, cancellationTokenSource.Token));
                    }

                    if (deleteTasks.Any((task) => task.IsFaulted || task.IsCanceled))
                    {
                        break;
                    }

                    numDeleted += deleteTasks.Where(x => x.IsCompletedSuccessfully).Sum(x => x.Result);
                    deleteTasks = deleteTasks.Where(task => !task.IsCompletedSuccessfully).ToList();

                    if (deleteTasks.Count >= MaxParallelThreads)
                    {
                        await deleteTasks[0];
                    }

                    if (!string.IsNullOrEmpty(ct) && (request.DeleteAll || (int)(request.MaxDeleteCount - numQueuedForDeletion) > 0))
                    {
                        using (var searchService = _searchServiceFactory.Invoke())
                        {
                            (matchedResults, ct) = await searchService.Value.ConditionalSearchAsync(
                                request.ResourceType,
                                request.ConditionalParameters,
                                cancellationToken,
                                request.DeleteAll ? searchCount : (int)(request.MaxDeleteCount - numQueuedForDeletion),
                                ct,
                                request.VersionType,
                                onlyIds: true);
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

            numDeleted += deleteTasks.Where(x => x.IsCompletedSuccessfully).Sum(x => x.Result);

            if (deleteTasks.Any((task) => task.IsFaulted || task.IsCanceled))
            {
                var exceptions = new List<Exception>();
                deleteTasks.Where((task) => task.IsFaulted || task.IsCanceled).ToList().ForEach((Task<long> result) =>
                    {
                        if (result.Exception != null)
                        {
                            // Count the number of resources deleted before the exception was thrown. Update the total.
                            result.Exception.InnerExceptions.Where((ex) => ex is IncompleteOperationException<long>).ToList().ForEach((ex) => numDeleted += ((IncompleteOperationException<long>)ex).PartialResults);
                            if (result.IsFaulted)
                            {
                                // Filter out noise from the cancellation exceptions caused by the core exception.
                                exceptions.AddRange(result.Exception.InnerExceptions.Where(e => e is not TaskCanceledException));
                            }
                        }
                    });
                var aggregateException = new AggregateException(exceptions);
                throw new IncompleteOperationException<long>(aggregateException, numDeleted);
            }

            return numDeleted;
        }

        private async Task<long> SoftDeleteResourcePage(ConditionalDeleteResourceRequest request, IReadOnlyCollection<SearchResultEntry> resourcesToDelete, CancellationToken cancellationToken)
        {
            await CreateAuditLog(request.ResourceType, request.DeleteOperation, false, resourcesToDelete.Select((item) => item.Resource.ResourceId));

            bool keepHistory = await _conformanceProvider.Value.CanKeepHistory(request.ResourceType, cancellationToken);
            ResourceWrapperOperation[] softDeletes = resourcesToDelete.Select(item =>
            {
                ResourceWrapper deletedWrapper = CreateSoftDeletedWrapper(request.ResourceType, item.Resource.ResourceId);
                return new ResourceWrapperOperation(deletedWrapper, true, keepHistory, null, false, false, bundleResourceContext: request.BundleResourceContext);
            }).ToArray();

            try
            {
                using var fhirDataStore = _fhirDataStoreFactory.Invoke();
                await fhirDataStore.Value.MergeAsync(softDeletes, cancellationToken);
            }
            catch (IncompleteOperationException<IDictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>> ex)
            {
                _logger.LogError(ex.InnerException, "Error soft deleting");

                var ids = ex.PartialResults.Select(item => item.Key.Id);
                await CreateAuditLog(request.ResourceType, request.DeleteOperation, true, ids);

                throw;
            }

            await CreateAuditLog(request.ResourceType, request.DeleteOperation, true, resourcesToDelete.Select((item) => item.Resource.ResourceId));

            return resourcesToDelete.Count;
        }

        private async Task<long> HardDeleteResourcePage(ConditionalDeleteResourceRequest request, IReadOnlyCollection<SearchResultEntry> resourcesToDelete, CancellationToken cancellationToken)
        {
            await CreateAuditLog(request.ResourceType, request.DeleteOperation, false, resourcesToDelete.Select((item) => item.Resource.ResourceId));

            var parallelBag = new ConcurrentBag<string>();
            try
            {
                using var fhirDataStore = _fhirDataStoreFactory.Invoke();

                // This throws AggrigateExceptions
                await Parallel.ForEachAsync(resourcesToDelete, cancellationToken, async (item, innerCt) =>
                {
                    await _retryPolicy.ExecuteAsync(async () => await fhirDataStore.Value.HardDeleteAsync(new ResourceKey(item.Resource.ResourceTypeName, item.Resource.ResourceId), request.DeleteOperation == DeleteOperation.PurgeHistory, innerCt));
                    parallelBag.Add(item.Resource.ResourceId);
                });
            }
            catch (Exception ex)
            {
                await CreateAuditLog(request.ResourceType, request.DeleteOperation, true, parallelBag);
                throw new IncompleteOperationException<long>(ex, parallelBag.Count);
            }

            await CreateAuditLog(request.ResourceType, request.DeleteOperation, true, parallelBag);

            return parallelBag.Count;
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

        private System.Threading.Tasks.Task CreateAuditLog(string resourceType, DeleteOperation operation, bool complete, IEnumerable<string> items, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            var auditTask = System.Threading.Tasks.Task.Run(() =>
            {
                AuditAction action = complete ? AuditAction.Executed : AuditAction.Executing;
                var context = _contextAccessor.RequestContext;
                var deleteAdditionalProperties = new Dictionary<string, string>();
                deleteAdditionalProperties["Affected Items"] = items.Aggregate(
                    (aggregate, item) =>
                    {
                        aggregate += ", " + item;
                        return aggregate;
                    });

                _auditLogger.LogAudit(
                    auditAction: action,
                    operation: operation.ToString(),
                    resourceType: resourceType,
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
    }
}
