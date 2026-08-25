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
using Hl7.Fhir.Specification;
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
    public class DeletionService : IDeletionService, IDisposable
    {
        private readonly IResourceWrapperFactory _resourceWrapperFactory;
        private readonly Lazy<IConformanceProvider> _conformanceProvider;
        private readonly IDeletionServiceDataStoreFactory _dataStoreFactory;
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
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly SemaphoreSlim _searchParamDeleteSemaphore;
        private bool _disposed;
        internal const string DefaultCallerAgent = "Microsoft.Health.Fhir.Server";
        private const int MaxParallelThreads = 64;

        public DeletionService(
            IResourceWrapperFactory resourceWrapperFactory,
            Lazy<IConformanceProvider> conformanceProvider,
            IDeletionServiceDataStoreFactory dataStoreFactory,
            IScopeProvider<ISearchService> searchServiceFactory,
            ResourceIdProvider resourceIdProvider,
            FhirRequestContextAccessor contextAccessor,
            IAuditLogger auditLogger,
            IOptions<CoreFeatureConfiguration> configuration,
            IFhirRuntimeConfiguration fhirRuntimeConfiguration,
            ISearchParameterOperations searchParameterOperations,
            IResourceDeserializer resourceDeserializer,
            ILogger<DeletionService> logger,
            IModelInfoProvider modelInfoProvider)
        {
            _resourceWrapperFactory = EnsureArg.IsNotNull(resourceWrapperFactory, nameof(resourceWrapperFactory));
            _conformanceProvider = EnsureArg.IsNotNull(conformanceProvider, nameof(conformanceProvider));
            _dataStoreFactory = EnsureArg.IsNotNull(dataStoreFactory, nameof(dataStoreFactory));
            _searchServiceFactory = EnsureArg.IsNotNull(searchServiceFactory, nameof(searchServiceFactory));
            _resourceIdProvider = EnsureArg.IsNotNull(resourceIdProvider, nameof(resourceIdProvider));
            _contextAccessor = EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));
            _auditLogger = EnsureArg.IsNotNull(auditLogger, nameof(auditLogger));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            _configuration = EnsureArg.IsNotNull(configuration.Value, nameof(configuration));
            _fhirRuntimeConfiguration = EnsureArg.IsNotNull(fhirRuntimeConfiguration, nameof(fhirRuntimeConfiguration));
            _searchParameterOperations = EnsureArg.IsNotNull(searchParameterOperations, nameof(searchParameterOperations));
            _resourceDeserializer = EnsureArg.IsNotNull(resourceDeserializer, nameof(resourceDeserializer));
            _modelInfoProvider = EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            _searchParamDeleteSemaphore = new SemaphoreSlim(1, 1);

            _retryPolicy = Policy
                .Handle<RequestRateExceededException>()
                .WaitAndRetryAsync(3, count => TimeSpan.FromSeconds(Math.Pow(2, count) + RandomNumberGenerator.GetInt32(0, 5)));
        }

        /// <summary>
        /// Deletes a single resource, honouring the client-supplied If-Match and any internally observed guard version.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Atomicity boundary for destructive deletes (<see cref="DeleteOperation.HardDelete"/> and
        /// <see cref="DeleteOperation.PurgeHistory"/>). <see cref="IFhirDataStore.HardDeleteAsync"/> accepts no version,
        /// so version enforcement is a deliberately non-atomic read-before-delete performed here rather than in the store.
        /// </para>
        /// <para>
        /// Guaranteed: no destructive store call is issued once a precondition is known to be violated at check time,
        /// including when the target has disappeared but a guard (If-Match or compared version) was supplied.
        /// </para>
        /// <para>
        /// Not guaranteed: a concurrent write landing between the read and <see cref="IFhirDataStore.HardDeleteAsync"/>
        /// is not detected, and the delete proceeds. Only <see cref="DeleteOperation.SoftDelete"/> enforces the version
        /// atomically, because it flows through <see cref="ResourceWrapperOperation"/> into the store's compare-and-swap.
        /// This non-atomic contract is intentional and approved; do not widen it without changing the store interface.
        /// </para>
        /// </remarks>
        public async Task<ResourceKey> DeleteAsync(DeleteResourceRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            ResourceKey key = request.ResourceKey;

            if (!string.IsNullOrEmpty(key.VersionId))
            {
                throw new MethodNotAllowedException(Core.Resources.DeleteVersionNotAllowed);
            }

            string version = null;
            using var scopedDataStore = _dataStoreFactory.GetScopedDataStore();
            var fhirDataStore = scopedDataStore.GetDataStore();
            bool requireETagOnUpdate = await _conformanceProvider.Value.RequireETag(key.ResourceType, cancellationToken);
            switch (request.DeleteOperation)
            {
                case DeleteOperation.SoftDelete:
                    ResourceWrapper deletedWrapper = CreateSoftDeletedWrapper(key.ResourceType, request.ResourceKey.Id);

                    bool keepHistory = await _conformanceProvider.Value.CanKeepHistory(key.ResourceType, cancellationToken);

                    UpsertOutcome result;
                    try
                    {
                        result = await _retryPolicy.ExecuteAsync(() => fhirDataStore.UpsertAsync(
                            new ResourceWrapperOperation(deletedWrapper, true, keepHistory, request.WeakETag, requireETagOnUpdate, false, bundleResourceContext: request.BundleResourceContext, comparedVersion: request.ComparedVersion),
                            cancellationToken));
                    }
                    catch (ResourceConflictException exception) when (
                        request.WeakETag != null &&
                        exception.WeakETag?.VersionId == request.WeakETag.VersionId &&
                        _modelInfoProvider.Version == FhirSpecification.Stu3)
                    {
                        throw new PreconditionFailedException(string.Format(Core.Resources.ResourceVersionConflict, request.WeakETag.VersionId));
                    }

                    version = result?.Wrapper.Version;
                    break;
                case DeleteOperation.HardDelete:
                    var resourceWrapper = await fhirDataStore.GetAsync(key, cancellationToken);
                    if (resourceWrapper != null)
                    {
                        ValidatePrecondition(key.ResourceType, resourceWrapper.Version, request.WeakETag, requireETagOnUpdate, request.ComparedVersion);
                    }
                    else
                    {
                        // The target has since disappeared (e.g. concurrently hard-deleted). HardDeleteAsync has no version
                        // parameter, so this remains a non-atomic read-before-delete check; failing here at least ensures the
                        // operation does not silently proceed as if a version-guarded target still existed.
                        ValidateMissingTargetPrecondition(request.WeakETag, request.ComparedVersion);
                    }

                    if (key.ResourceType == KnownResourceTypes.SearchParameter)
                    {
                        if (resourceWrapper != null && !resourceWrapper.IsDeleted)
                        {
                            await _retryPolicy.ExecuteAsync(async () => await _searchParameterOperations.DeleteSearchParameterAsync(resourceWrapper.RawResource, cancellationToken, ignoreSearchParameterNotSupportedException: true, isHardDelete: true));
                        }

                        break;
                    }

                    await _retryPolicy.ExecuteAsync(async () => await fhirDataStore.HardDeleteAsync(key, false, request.AllowPartialSuccess, cancellationToken));
                    break;
                case DeleteOperation.PurgeHistory:
                    var currentResourceWrapper = await fhirDataStore.GetAsync(key, cancellationToken);
                    if (currentResourceWrapper != null)
                    {
                        ValidatePrecondition(key.ResourceType, currentResourceWrapper.Version, request.WeakETag, requireETagOnUpdate, request.ComparedVersion);
                    }
                    else
                    {
                        // See HardDelete case above: HardDeleteAsync has no version parameter, so this is a non-atomic read-before-delete check.
                        ValidateMissingTargetPrecondition(request.WeakETag, request.ComparedVersion);
                    }

                    await _retryPolicy.ExecuteAsync(async () => await fhirDataStore.HardDeleteAsync(key, true, request.AllowPartialSuccess, cancellationToken));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(request));
            }

            return new ResourceKey(key.ResourceType, key.Id, version);
        }

        /// <summary>
        /// Enforces version preconditions for a destructive delete whose target no longer exists.
        /// </summary>
        /// <remarks>
        /// Precedence intentionally mirrors <see cref="ValidatePrecondition"/>: the client-supplied If-Match is
        /// reported first so the caller always sees the version it actually sent, and the internally observed
        /// <paramref name="comparedVersion"/> is only reported when no If-Match was supplied. When neither guard is
        /// present the request is a plain unconditional delete and must stay idempotent, so nothing is thrown.
        /// </remarks>
        private static void ValidateMissingTargetPrecondition(WeakETag weakETag, string comparedVersion)
        {
            if (weakETag != null)
            {
                throw new PreconditionFailedException(string.Format(Core.Resources.ResourceVersionConflict, weakETag.VersionId));
            }

            if (!string.IsNullOrEmpty(comparedVersion))
            {
                throw new PreconditionFailedException(string.Format(Core.Resources.ResourceVersionConflict, comparedVersion));
            }
        }

        /// <summary>
        /// Resolves the version observed for the single Match of a guarded conditional delete, failing if it is unavailable.
        /// </summary>
        /// <remarks>
        /// The guard for a single-Match conditional delete is the version the conditional search itself observed. If the
        /// data store projection did not surface one, the precondition cannot be evaluated at all, and silently continuing
        /// would downgrade a guarded delete into an unguarded one that mutates Includes. Callers must invoke this before
        /// any SearchParameter status change, Include mutation or reference removal. The message identifies the resource
        /// rather than formatting an absent version, so it never renders an empty expected version.
        /// </remarks>
        private static string GetGuardedMatchVersion(SearchResultEntry singleMatch)
        {
            ResourceWrapper matchResource = singleMatch.Resource;
            string observedVersion = matchResource?.Version;

            if (string.IsNullOrEmpty(observedVersion))
            {
                throw new PreconditionFailedException(string.Format(
                    Core.Resources.ConditionalDeleteMatchVersionUnavailable,
                    matchResource?.ResourceTypeName,
                    matchResource?.ResourceId));
            }

            return observedVersion;
        }

        /// <summary>
        /// Resolves the guarded Match and the version it was observed at for a conditional delete page, or
        /// (<c>default</c>, <c>null</c>) when the page is unguarded (not a single-resource conditional delete).
        /// </summary>
        /// <remarks>
        /// Callers must resolve this before building any wrapper operation or mutating any Include, so an
        /// unavailable version (see <see cref="GetGuardedMatchVersion"/>) fails ahead of every mutation.
        /// </remarks>
        private static (SearchResultEntry GuardedMatch, string GuardedMatchVersion) ResolveGuardedMatch(
            bool isSingleResourceConditionalDelete,
            IReadOnlyCollection<SearchResultEntry> resourcesToDelete)
        {
            if (!isSingleResourceConditionalDelete)
            {
                return (default, null);
            }

            SearchResultEntry guardedMatch = resourcesToDelete.Single(resource => resource.SearchEntryMode == ValueSets.SearchEntryMode.Match);
            return (guardedMatch, GetGuardedMatchVersion(guardedMatch));
        }

        private void ValidatePrecondition(string resourceType, string currentVersion, WeakETag weakETag, bool requireETag, string comparedVersion = null)
        {
            if (requireETag && weakETag == null)
            {
                string message = string.Format(Core.Resources.IfMatchHeaderRequiredForResource, resourceType);

                if (_modelInfoProvider.Version == FhirSpecification.Stu3)
                {
                    throw new PreconditionFailedException(message);
                }

                throw new BadRequestException(message);
            }

            if (weakETag != null && weakETag.VersionId != currentVersion)
            {
                throw new PreconditionFailedException(string.Format(Core.Resources.ResourceVersionConflict, weakETag.VersionId));
            }

            // comparedVersion is the version internally observed while resolving a guarded (single-match) conditional
            // delete. It is never derived from, or a substitute for, the client-supplied weakETag above, and it is
            // only ever set for the single Match resource of a guarded conditional delete -- never for Include
            // resources or when multiple resources matched. If the resource has since been updated, fail the whole
            // operation before any Include is mutated.
            if (!string.IsNullOrEmpty(comparedVersion) && !string.Equals(comparedVersion, currentVersion, StringComparison.Ordinal))
            {
                throw new PreconditionFailedException(string.Format(Core.Resources.ResourceVersionConflict, comparedVersion));
            }
        }

        private static bool IsSingleResourceConditionalDelete(ConditionalDeleteResourceRequest request, IReadOnlyCollection<SearchResultEntry> resources)
        {
            return request.IsSingleResourceConditionalDelete
                && resources.Count(resource => resource.SearchEntryMode == ValueSets.SearchEntryMode.Match) == 1;
        }

        private static void ThrowIfSingleMatchOperationFailed(MergeOutcome mergeOutcome, ResourceWrapperOperation operation)
        {
            if (mergeOutcome == null)
            {
                return;
            }

            DataStoreOperationOutcome outcome = mergeOutcome.Results
                .Where(result =>
                    string.Equals(result.Key.ResourceType, operation.Wrapper.ResourceTypeName, StringComparison.Ordinal) &&
                    string.Equals(result.Key.Id, operation.Wrapper.ResourceId, StringComparison.Ordinal))
                .Select(result => result.Value)
                .SingleOrDefault();

            if (outcome?.Exception != null)
            {
                throw outcome.Exception;
            }
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
            if (!request.IsIncludesRequest && AreIncludeResultsTruncated(ict))
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
            List<Exception> exceptionsOutsideTasks = new List<Exception>();
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
                        if (!request.IsIncludesRequest && AreIncludeResultsTruncated(ict))
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
                exceptionsOutsideTasks.Add(ex);
                await cancellationTokenSource.CancelAsync();
            }

            try
            {
                // We need to wait until all running tasks are cancelled to get a count of resources deleted.
                await Task.WhenAll(deleteTasks);
            }
            catch (FhirException) when (request.IsSingleResourceConditionalDelete)
            {
                throw;
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

            if (deleteTasks.Any((task) => task.IsFaulted || task.IsCanceled) || tooManyIncludeResults || exceptionsOutsideTasks.Any())
            {
                var exceptions = new List<Exception>(exceptionsOutsideTasks);

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
                throw new IncompleteOperationException<IDictionary<string, long>>(aggregateException, resourceTypesDeleted);
            }

            return resourceTypesDeleted;
        }

        private async Task<Dictionary<string, long>> SoftDeleteResourcePage(ConditionalDeleteResourceRequest request, IReadOnlyCollection<SearchResultEntry> resourcesToDelete, CancellationToken cancellationToken)
        {
            var guid = Guid.NewGuid();
            bool isSingleResourceConditionalDelete = IsSingleResourceConditionalDelete(request, resourcesToDelete);

            // Resolved before any wrapper operation is built so an unavailable version fails ahead of every mutation.
            (SearchResultEntry guardedMatch, string guardedMatchVersion) = ResolveGuardedMatch(isSingleResourceConditionalDelete, resourcesToDelete);

            _logger.LogInformation("Soft deleting {Count} resources with request {RequestId}", resourcesToDelete.Count, guid);

            await CreateAuditLog(
                request.ResourceType,
                request.DeleteOperation,
                false,
                resourcesToDelete.Select((item) => (item.Resource.ResourceTypeName, item.Resource.ResourceId, item.SearchEntryMode == ValueSets.SearchEntryMode.Include)));

            var softDeleteIncludes = await Task.WhenAll(resourcesToDelete.Where(resource => resource.SearchEntryMode == ValueSets.SearchEntryMode.Include).Select(async item =>
            {
                // If there isn't a cached capability statement (IE this is the first request made after a service starts up) then performance on this request will be terrible as the capability statement needs to be rebuilt for every resource.
                // This is because the capability statement can't be made correctly in a background job, so it doesn't cache the result.
                // The result is good enough for background work, but can't be used for metadata as the urls aren't formated properly.
                bool keepHistory = await _conformanceProvider.Value.CanKeepHistory(item.Resource.ResourceTypeName, cancellationToken);
                ResourceWrapper deletedWrapper = CreateSoftDeletedWrapper(item.Resource.ResourceTypeName, item.Resource.ResourceId);
                return new ResourceWrapperOperation(deletedWrapper, true, keepHistory, null, false, false, bundleResourceContext: request.BundleResourceContext);
            }));

            var softDeleteMatches = await Task.WhenAll(resourcesToDelete.Where(resource => resource.SearchEntryMode == ValueSets.SearchEntryMode.Match && resource.Resource.ResourceTypeName != KnownResourceTypes.SearchParameter).Select(async item =>
            {
                bool keepHistory = await _conformanceProvider.Value.CanKeepHistory(item.Resource.ResourceTypeName, cancellationToken);
                bool requireETagOnUpdate = isSingleResourceConditionalDelete &&
                    await _conformanceProvider.Value.RequireETag(item.Resource.ResourceTypeName, cancellationToken);
                ResourceWrapper deletedWrapper = CreateSoftDeletedWrapper(item.Resource.ResourceTypeName, item.Resource.ResourceId);
                return new ResourceWrapperOperation(
                    deletedWrapper,
                    true,
                    keepHistory,
                    isSingleResourceConditionalDelete ? request.WeakETag : null,
                    requireETagOnUpdate,
                    false,
                    bundleResourceContext: request.BundleResourceContext,
                    comparedVersion: guardedMatchVersion);
            }));

            var partialResults = new List<(string ResourceType, string ResourceId, bool IsInclude)>();
            try
            {
                using var scopedDataStore = _dataStoreFactory.GetScopedDataStore();
                var fhirDataStore = scopedDataStore.GetDataStore();

                var matchResourcesToDelete = resourcesToDelete.Where(resource => resource.SearchEntryMode == ValueSets.SearchEntryMode.Match).ToList();
                if (isSingleResourceConditionalDelete && guardedMatch.Resource.ResourceTypeName == KnownResourceTypes.SearchParameter)
                {
                    bool requireETagOnUpdate = await _conformanceProvider.Value.RequireETag(guardedMatch.Resource.ResourceTypeName, cancellationToken);
                    ResourceWrapper currentResource = await fhirDataStore.GetAsync(
                        new ResourceKey(guardedMatch.Resource.ResourceTypeName, guardedMatch.Resource.ResourceId),
                        cancellationToken);
                    if (currentResource != null)
                    {
                        // SearchParameter deletes bypass ResourceWrapperOperation, so validate before changing its status.
                        ValidatePrecondition(guardedMatch.Resource.ResourceTypeName, currentResource.Version, request.WeakETag, requireETagOnUpdate, guardedMatchVersion);
                    }
                    else
                    {
                        // The guarded Match has disappeared (e.g. concurrently deleted) since the conditional search observed it.
                        // SearchParameter deletes bypass ResourceWrapperOperation/MergeAsync entirely, so this read-then-act check
                        // is the only enforcement point and remains non-atomic; failing here still guarantees no Include is mutated.
                        ValidateMissingTargetPrecondition(request.WeakETag, guardedMatchVersion);
                    }
                }

                async Task DeleteMatchesAsync(bool trackPartialResults)
                {
                    await DeleteSearchParametersAsync(matchResourcesToDelete, cancellationToken);

                    if (softDeleteMatches.Any())
                    {
                        MergeOutcome mergeOutcome = await fhirDataStore.MergeAsync(softDeleteMatches, cancellationToken);
                        if (isSingleResourceConditionalDelete)
                        {
                            ThrowIfSingleMatchOperationFailed(mergeOutcome, softDeleteMatches.Single());
                        }
                    }

                    if (trackPartialResults)
                    {
                        partialResults.AddRange(softDeleteMatches.Select(item => (item.Wrapper.ResourceTypeName, item.Wrapper.ResourceId, false)));
                    }
                }

                if (isSingleResourceConditionalDelete)
                {
                    // Persist the guarded target before Includes so a version failure cannot mutate Includes.
                    await DeleteMatchesAsync(trackPartialResults: true);
                }

                if (softDeleteIncludes.Any())
                {
                    // Includes are always merged here; for unguarded requests this happens before matches so an Include failure leaves matches intact for restart.
                    await fhirDataStore.MergeAsync(softDeleteIncludes, cancellationToken);
                    partialResults.AddRange(softDeleteIncludes.Select(item => (
                        item.Wrapper.ResourceTypeName,
                        item.Wrapper.ResourceId,
                        resourcesToDelete
                            .Where(resource => resource.Resource.ResourceId == item.Wrapper.ResourceId && resource.Resource.ResourceTypeName == item.Wrapper.ResourceTypeName)
                            .FirstOrDefault().SearchEntryMode == ValueSets.SearchEntryMode.Include)));
                }

                if (!isSingleResourceConditionalDelete)
                {
                    await DeleteMatchesAsync(trackPartialResults: false);
                }
            }
            catch (FhirException) when (isSingleResourceConditionalDelete)
            {
                throw;
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error soft deleting");
                await CreateAuditLog(request.ResourceType, request.DeleteOperation, true, partialResults);
                throw new IncompleteOperationException<Dictionary<string, long>>(
                    ex,
                    partialResults.GroupBy(pair => pair.ResourceType).ToDictionary(group => group.Key, group => (long)group.Count()));
            }

            await CreateAuditLog(
                request.ResourceType,
                request.DeleteOperation,
                true,
                resourcesToDelete.Select((item) => (item.Resource.ResourceTypeName, item.Resource.ResourceId, item.SearchEntryMode == ValueSets.SearchEntryMode.Include)));

            var results = resourcesToDelete.GroupBy(x => x.Resource.ResourceTypeName).ToDictionary(x => x.Key, x => (long)x.Count());
            _logger.LogInformation("Soft deleted {Count} resources with request {RequestId}", results.Sum(x => x.Value), guid);

            return results;
        }

        private async Task<Dictionary<string, long>> HardDeleteResourcePage(ConditionalDeleteResourceRequest request, IReadOnlyCollection<SearchResultEntry> resourcesToDelete, CancellationToken cancellationToken)
        {
            var guid = Guid.NewGuid();
            bool isSingleResourceConditionalDelete = IsSingleResourceConditionalDelete(request, resourcesToDelete);

            // Resolved before RemoveReferences and before any Include is deleted so an unavailable version fails ahead of every mutation.
            (SearchResultEntry guardedMatch, string guardedMatchVersion) = ResolveGuardedMatch(isSingleResourceConditionalDelete, resourcesToDelete);

            _logger.LogInformation("Hard deleting {Count} resources with request {RequestId}", resourcesToDelete.Count, guid);

            await CreateAuditLog(
                request.ResourceType,
                request.DeleteOperation,
                false,
                resourcesToDelete.Select((item) => (item.Resource.ResourceTypeName, item.Resource.ResourceId, item.SearchEntryMode == ValueSets.SearchEntryMode.Include)));

            var parallelBag = new ConcurrentBag<(string, string, bool)>();
            try
            {
                using var scopedDataStore = _dataStoreFactory.GetScopedDataStore();
                var fhirDataStore = scopedDataStore.GetDataStore();

                var includedResources = resourcesToDelete.Where(resource => resource.SearchEntryMode == ValueSets.SearchEntryMode.Include).ToList();
                var matchedResources = resourcesToDelete.Where(resource => resource.SearchEntryMode == ValueSets.SearchEntryMode.Match).ToList();
                bool requireETagOnUpdate = isSingleResourceConditionalDelete &&
                    await _conformanceProvider.Value.RequireETag(request.ResourceType, cancellationToken);

                if (isSingleResourceConditionalDelete)
                {
                    var key = new ResourceKey(guardedMatch.Resource.ResourceTypeName, guardedMatch.Resource.ResourceId);
                    ResourceWrapper currentResource = await fhirDataStore.GetAsync(key, cancellationToken);
                    if (currentResource != null)
                    {
                        // Same non-atomic read-before-delete boundary documented on DeleteAsync: HardDeleteAsync has no
                        // version parameter, so a write landing after this read is not detected.
                        ValidatePrecondition(guardedMatch.Resource.ResourceTypeName, currentResource.Version, request.WeakETag, requireETagOnUpdate, guardedMatchVersion);
                    }
                    else
                    {
                        // The guarded Match has disappeared (e.g. concurrently deleted) since the conditional search observed it.
                        // HardDeleteAsync has no version parameter, so this read-then-act check is the only enforcement point and
                        // remains non-atomic; failing here still guarantees no Include is mutated (Includes are processed below).
                        // guardedMatchVersion is never empty here, so this always throws.
                        ValidateMissingTargetPrecondition(request.WeakETag, guardedMatchVersion);
                    }
                }

                if (request.RemoveReferences)
                {
                    foreach (var item in resourcesToDelete)
                    {
                        await RemoveReferences(item, request, cancellationToken);
                    }
                }

                async Task DeleteResourceAsync(SearchResultEntry item, CancellationToken innerCt)
                {
                    if (item.Resource.ResourceTypeName == KnownResourceTypes.SearchParameter)
                    {
                        if (request.DeleteOperation == DeleteOperation.PurgeHistory)
                        {
                            await _retryPolicy.ExecuteAsync(async () => await fhirDataStore.HardDeleteAsync(new ResourceKey(item.Resource.ResourceTypeName, item.Resource.ResourceId), keepCurrentVersion: true, request.AllowPartialSuccess, innerCt));
                        }
                        else
                        {
                            await DeleteSearchParameterWithLockAsync(item, true, innerCt);
                        }
                    }
                    else
                    {
                        await _retryPolicy.ExecuteAsync(async () => await fhirDataStore.HardDeleteAsync(new ResourceKey(item.Resource.ResourceTypeName, item.Resource.ResourceId), request.DeleteOperation == DeleteOperation.PurgeHistory, request.AllowPartialSuccess, innerCt));
                    }

                    parallelBag.Add((item.Resource.ResourceTypeName, item.Resource.ResourceId, item.SearchEntryMode == ValueSets.SearchEntryMode.Include));
                }

                if (isSingleResourceConditionalDelete)
                {
                    // Delete the guarded target first so a stale precondition cannot mutate Includes.
                    await DeleteResourceAsync(guardedMatch, cancellationToken);
                }

                // Includes are always deleted here; for unguarded requests this happens before matches so an Include failure leaves matches intact for restart.
                // This throws AggregateExceptions.
                // Note: includedResources cannot have search params, so DeleteResourceAsync always takes the non-SearchParameter path for them.
                await Parallel.ForEachAsync(includedResources, cancellationToken, async (item, innerCt) => await DeleteResourceAsync(item, innerCt));

                if (!isSingleResourceConditionalDelete)
                {
                    await Parallel.ForEachAsync(
                        matchedResources.Where(_ => _.Resource.ResourceTypeName != KnownResourceTypes.SearchParameter),
                        cancellationToken,
                        async (item, innerCt) => await DeleteResourceAsync(item, innerCt));

                    // SearchParameter handling depends on whether this is PurgeHistory or HardDelete.
                    foreach (var item in matchedResources.Where(_ => _.Resource.ResourceTypeName == KnownResourceTypes.SearchParameter))
                    {
                        await DeleteResourceAsync(item, cancellationToken);
                    }
                }
            }
            catch (FhirException) when (isSingleResourceConditionalDelete)
            {
                throw;
            }
            catch (Exception ex)
            {
                await CreateAuditLog(request.ResourceType, request.DeleteOperation, true, parallelBag);
                throw new IncompleteOperationException<Dictionary<string, long>>(ex, parallelBag.GroupBy(x => x.Item1).ToDictionary(x => x.Key, x => (long)x.Count()));
            }

            await CreateAuditLog(request.ResourceType, request.DeleteOperation, true, parallelBag);

            var results = parallelBag.GroupBy(x => x.Item1).ToDictionary(x => x.Key, x => (long)x.Count());
            _logger.LogInformation("Hard deleted {Count} resources with request {RequestId}", results.Sum(x => x.Value), guid);

            return results;
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

                Uri uri = null;

                try
                {
                    uri = context.Uri;
                }
                catch (UriFormatException ex)
                {
                    _logger.LogWarning(ex, "Failed to read request URI from the request context during delete audit logging.");
                }

                var batches = BulkOperationAuditLogHelper.CreateAffectedItemBatches(items.ToList());

                foreach (var batch in batches)
                {
                    var deleteAdditionalProperties = new Dictionary<string, string>();
                    deleteAdditionalProperties["Affected Items"] = batch;

                    _auditLogger.LogAudit(
                        auditAction: action,
                        operation: operation.ToString(),
                        resourceType: primaryResourceType,
                        requestUri: uri,
                        statusCode: statusCode,
                        correlationId: context.CorrelationId,
                        callerIpAddress: string.Empty,
                        callerClaims: null,
                        customHeaders: null,
                        operationType: string.Empty,
                        callerAgent: DefaultCallerAgent,
                        additionalProperties: deleteAdditionalProperties);
                }
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
                        var wrapper = _resourceWrapperFactory.Create(reference, deleted: false, keepMeta: true);
                        modifiedResources.Add(new ResourceWrapperOperation(
                            wrapper,
                            allowCreate: false,
                            keepHistory: true,
                            weakETag: null,
                            requireETagOnUpdate: false,
                            keepVersion: false,
                            bundleResourceContext: request.BundleResourceContext));
                    }

                    using var scopedDataStore = _dataStoreFactory.GetScopedDataStore();
                    var fhirDataStore = scopedDataStore.GetDataStore();
                    await fhirDataStore.MergeAsync(modifiedResources, cancellationToken);

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

        /// <summary>
        /// Returns true if the include results are truncated.
        /// The bundle issue being checked is only used in Cosmos DB as the $includes operation isn't supported there.
        /// In SQL Server the includes continuation token is used to determine if there are more include results.
        /// </summary>
        /// <param name="ict">The includes continuation token, also seen as the related link. If this has a value than the include results are truncated.</param>
        /// <returns>True if the include results are truncated; otherwise, false.</returns>
        private bool AreIncludeResultsTruncated(string ict)
        {
            return _contextAccessor.RequestContext.BundleIssues.Any(x => string.Equals(x.Diagnostics, Core.Resources.TruncatedIncludeMessage, StringComparison.OrdinalIgnoreCase))
                || !string.IsNullOrEmpty(ict);
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

        private async Task DeleteSearchParametersAsync(IEnumerable<SearchResultEntry> entries, CancellationToken cancellationToken)
        {
            foreach (var entry in entries.Where(_ => _.Resource.ResourceTypeName == KnownResourceTypes.SearchParameter))
            {
                await DeleteSearchParameterWithLockAsync(entry, false, cancellationToken);
            }
        }

        private async Task DeleteSearchParameterWithLockAsync(SearchResultEntry item, bool isHardDelete, CancellationToken cancellationToken)
        {
            await _searchParamDeleteSemaphore.WaitAsync(cancellationToken);
            try
            {
                await SearchParameterRetry.ExecuteAsync(
                    async () =>
                    {
                        await _searchParameterOperations.DeleteSearchParameterAsync(item.Resource.RawResource, cancellationToken, ignoreSearchParameterNotSupportedException: true, isHardDelete: isHardDelete);
                    },
                    "Deletion");
            }
            finally
            {
                _searchParamDeleteSemaphore.Release();
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _searchParamDeleteSemaphore?.Dispose();
                }

                _disposed = true;
            }
        }
    }
}
