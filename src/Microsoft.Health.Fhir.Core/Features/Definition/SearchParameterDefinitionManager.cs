// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition.BundleWrappers;
using Microsoft.Health.Fhir.Core.Features.Health;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Messages.CapabilityStatement;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Messages.Storage;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Definition
{
    /// <summary>
    /// Provides mechanism to access search parameter definition.
    /// </summary>
    public class SearchParameterDefinitionManager : ISearchParameterDefinitionManager, INotificationHandler<SearchParametersUpdatedNotification>, INotificationHandler<StorageInitializedNotification>, IDisposable
    {
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly IMediator _mediator;
        private readonly ConcurrentDictionary<string, string> _resourceTypeSearchParameterHashMap;
        private readonly IScopeProvider<ISearchService> _searchServiceFactory;
        private readonly ISearchParameterComparer<SearchParameterInfo> _searchParameterComparer;
        private readonly IScopeProvider<ISearchParameterStatusDataStore> _searchParameterStatusDataStoreFactory;
        private ISearchParameterStatusDataStore _statusDataStore;
        private readonly ISearchParameterSupportResolver _searchParameterSupportResolver;
        private readonly ILogger _logger;
        private DateTimeOffset? _searchParamLastUpdated;
        private readonly SemaphoreSlim _refreshSemaphore;
        private readonly int _maxSecondsToWaitForSemaphore = 100;
        private readonly List<string> _enabledSortIndices = new List<string>() { "http://hl7.org/fhir/SearchParameter/individual-birthdate", "http://hl7.org/fhir/SearchParameter/individual-family", "http://hl7.org/fhir/SearchParameter/individual-given" };

        private bool _initialized = false;

        public SearchParameterDefinitionManager(
            IModelInfoProvider modelInfoProvider,
            IMediator mediator,
            IScopeProvider<ISearchService> searchServiceFactory,
            ISearchParameterComparer<SearchParameterInfo> searchParameterComparer,
            IScopeProvider<ISearchParameterStatusDataStore> searchParameterStatusDataStoreFactory,
            ISearchParameterSupportResolver searchParameterSupportResolver,
            ILogger<SearchParameterDefinitionManager> logger)
        {
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(searchServiceFactory, nameof(searchServiceFactory));
            EnsureArg.IsNotNull(searchParameterComparer, nameof(searchParameterComparer));
            EnsureArg.IsNotNull(searchParameterStatusDataStoreFactory, nameof(searchParameterStatusDataStoreFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _modelInfoProvider = modelInfoProvider;
            _mediator = mediator;
            _resourceTypeSearchParameterHashMap = new ConcurrentDictionary<string, string>();
            TypeLookup = new ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentQueue<string>>>();
            UrlLookup = new ConcurrentDictionary<string, SearchParameterInfo>();
            _searchServiceFactory = searchServiceFactory;
            _searchParameterStatusDataStoreFactory = searchParameterStatusDataStoreFactory;
            _searchParameterComparer = searchParameterComparer;
            _searchParameterSupportResolver = EnsureArg.IsNotNull(searchParameterSupportResolver, nameof(searchParameterSupportResolver));
            _logger = logger;
            _refreshSemaphore = new SemaphoreSlim(1, 1);

            var bundle = SearchParameterDefinitionBuilder.ReadEmbeddedSearchParameters("search-parameters.json", _modelInfoProvider);
            var msBundle = SearchParameterDefinitionBuilder.ReadEmbeddedSearchParameters("ms-search-parameters.json", _modelInfoProvider);

            var searchParamResources = bundle.Entries.Select(e => e.Resource).ToList();
            searchParamResources.AddRange(msBundle.Entries.Select(e => e.Resource));

            SearchParameterDefinitionBuilder.Build(
                searchParamResources,
                UrlLookup,
                TypeLookup,
                _modelInfoProvider,
                _searchParameterComparer,
                _logger,
                isSystemDefined: true);
        }

        public DateTimeOffset? SearchParamLastUpdated => _searchParamLastUpdated;

        internal ConcurrentDictionary<string, SearchParameterInfo> UrlLookup { get; set; }

        // TypeLookup: Resource type -> code -> ordered queue of definition URLs.
        internal ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentQueue<string>>> TypeLookup { get; set; }

        public IEnumerable<SearchParameterInfo> AllSearchParameters => UrlLookup.Values;

        public IReadOnlyDictionary<string, string> SearchParameterHashMap
        {
            get { return new ReadOnlyDictionary<string, string>(_resourceTypeSearchParameterHashMap.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)); }
        }

        public async Task EnsureInitializedAsync(CancellationToken cancellationToken)
        {
            try
            {
                _statusDataStore = _searchParameterStatusDataStoreFactory.Invoke().Value;
                await GetAndApplySearchParameterUpdates(cancellationToken);
                await SetStatesOfKnownParameters(cancellationToken);
                await _mediator.Publish(new SearchParameterDefinitionManagerInitialized(), cancellationToken);
                _initialized = true;
            }
            catch
            {
                _initialized = false;
                throw;
            }
        }

        public void EnsureInitialized()
        {
            if (!_initialized)
            {
                _logger.LogWarning("Search parameters are not initialized.");

                // throw new InitializationException("Failed to initialize search parameters");
            }
        }

        public IEnumerable<SearchParameterInfo> GetSearchParameters(string resourceType)
        {
            EnsureInitialized();

            if (TypeLookup.TryGetValue(resourceType, out ConcurrentDictionary<string, ConcurrentQueue<string>> value))
            {
                return value.Values
                    .SelectMany(x => x)
                    .Where(uri => UrlLookup.TryGetValue(uri, out _))
                    .Select(uri => UrlLookup[uri])
                    .ToList();
            }

            throw new ResourceNotSupportedException(resourceType);
        }

        public SearchParameterInfo GetSearchParameter(string resourceType, string code)
        {
            EnsureInitialized();

            if (TryGetFromTypeLookup(resourceType, code, out SearchParameterInfo searchParameter))
            {
                return searchParameter;
            }

            throw new SearchParameterNotSupportedException(resourceType, code);
        }

        public bool TryGetSearchParameter(string resourceType, string code, out SearchParameterInfo searchParameter)
        {
            EnsureInitialized();

            return TryGetFromTypeLookup(resourceType, code, out searchParameter);
        }

        public bool TryGetSearchParameter(string resourceType, string code, bool excludePendingDelete, out SearchParameterInfo searchParameter)
        {
            EnsureInitialized();

            if (TryGetFromTypeLookup(resourceType, code, out searchParameter))
            {
                if (excludePendingDelete && searchParameter.SearchParameterStatus == SearchParameterStatus.PendingDelete)
                {
                    searchParameter = null;
                    return false;
                }

                return true;
            }

            return false;
        }

        public SearchParameterInfo GetSearchParameter(string definitionUri)
        {
            EnsureInitialized();
            if (UrlLookup.TryGetValue(definitionUri, out SearchParameterInfo value))
            {
                return value;
            }

            throw new SearchParameterNotSupportedException(definitionUri);
        }

        public bool TryGetSearchParameter(string definitionUri, out SearchParameterInfo value)
        {
            EnsureInitialized();
            return UrlLookup.TryGetValue(definitionUri, out value);
        }

        public bool TryGetSearchParameter(string definitionUri, bool excludePendingDelete, out SearchParameterInfo value)
        {
            EnsureInitialized();
            if (UrlLookup.TryGetValue(definitionUri, out value))
            {
                if (excludePendingDelete && value.SearchParameterStatus == SearchParameterStatus.PendingDelete)
                {
                    value = null;
                    return false;
                }

                return true;
            }

            return false;
        }

        public string GetSearchParameterHashForResourceType(string resourceType)
        {
            EnsureInitialized();
            EnsureArg.IsNotNullOrWhiteSpace(resourceType, nameof(resourceType));

            if (_resourceTypeSearchParameterHashMap.TryGetValue(resourceType, out string hash))
            {
                return hash;
            }

            return null;
        }

        public void UpdateSearchParameterHashMap(Dictionary<string, string> updatedSearchParamHashMap)
        {
            EnsureArg.IsNotNull(updatedSearchParamHashMap, nameof(updatedSearchParamHashMap));

            foreach (KeyValuePair<string, string> kvp in updatedSearchParamHashMap)
            {
                _resourceTypeSearchParameterHashMap.AddOrUpdate(
                    kvp.Key,
                    kvp.Value,
                    (resourceType, existingValue) => kvp.Value);
            }
        }

        public void AddNewSearchParameters(IReadOnlyCollection<ITypedElement> searchParameters, bool calculateHash = true)
        {
            SearchParameterDefinitionBuilder.Build(
                searchParameters,
                UrlLookup,
                TypeLookup,
                _modelInfoProvider,
                _searchParameterComparer,
                _logger);

            if (calculateHash)
            {
                CalculateSearchParameterHash();
            }
        }

        private void CalculateSearchParameterHash()
        {
            foreach (string resourceName in TypeLookup.Keys)
            {
                var searchParameters = GetSearchParameters(resourceName);
                if (searchParameters.Any())
                {
                    string searchParamHash = searchParameters.CalculateSearchParameterHash();
                    _resourceTypeSearchParameterHashMap.AddOrUpdate(
                        resourceName,
                        searchParamHash,
                        (resourceType, existingValue) => searchParamHash);
                }
            }
        }

        public void DeleteSearchParameter(ITypedElement searchParam)
        {
            var searchParamWrapper = new SearchParameterWrapper(searchParam);
            DeleteSearchParameter(searchParamWrapper.Url);
        }

        private void DeleteSearchParameterIfExists(string url, bool calculateHash = true)
        {
            try
            {
                DeleteSearchParameter(url, false);
            }
            catch (ResourceNotFoundException)
            {
                // do nothing, there may not be a search parameter to remove
            }
        }

        public void DeleteSearchParameter(string url, bool calculateHash = true)
        {
            if (!UrlLookup.TryRemove(url, out SearchParameterInfo searchParameterInfo))
            {
                throw new ResourceNotFoundException(string.Format(Core.Resources.CustomSearchParameterNotfound, url));
            }

            // find all derived resources from the list of base resources
            var allResourceTypes = GetDerivedResourceTypes(searchParameterInfo.BaseResourceTypes);
            var updated = false;
            foreach (var resourceType in allResourceTypes)
            {
                if (TypeLookup.TryGetValue(resourceType, out var lookup) &&
                    lookup.TryGetValue(searchParameterInfo.Code, out var q) &&
                    q.Any(x => string.Equals(x, url, StringComparison.Ordinal)))
                {
                    var newq = new ConcurrentQueue<string>(q.Where(x => !string.Equals(x, url, StringComparison.Ordinal)));
                    if (lookup.TryUpdate(searchParameterInfo.Code, newq, q))
                    {
                        updated = true;
                    }
                    else
                    {
                        _logger.LogError("Failed to remove a search parameter from TypeLookup: {Url}, {ResourceType}, {Code}", url, resourceType, searchParameterInfo.Code);
                    }
                }
            }

            if (calculateHash && updated)
            {
                CalculateSearchParameterHash();
            }
        }

        public void UpdateSearchParameterStatus(string url, SearchParameterStatus desiredStatus)
        {
            if (UrlLookup.TryGetValue(url, out var searchParameterInfo))
            {
                searchParameterInfo.SearchParameterStatus = desiredStatus;
            }
        }

        public async Task Handle(SearchParametersUpdatedNotification notification, CancellationToken cancellationToken)
        {
            var retry = 0;
            while (retry < 3)
            {
                try
                {
                    _logger.LogInformation("SearchParameterDefinitionManager: Search parameters updated");
                    CalculateSearchParameterHash();
                    await _mediator.Publish(new RebuildCapabilityStatement(RebuildPart.SearchParameter), cancellationToken);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error calculating search parameter hash. Retry {retry}");
                    retry++;
                }
            }

            // Not reporting notification while we investigate why this could happen
            // await _mediator.Publish(new ImproperBehaviorNotification("Error calculating search parameter hash"), cancellationToken);
        }

        public async Task Handle(StorageInitializedNotification notification, CancellationToken cancellationToken)
        {
            var retry = 0;
            while (retry < 3)
            {
                try
                {
                    _logger.LogInformation("SearchParameterDefinitionManager: Storage initialized");
                    await EnsureInitializedAsync(cancellationToken);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error initializing search parameters. Retry {retry}");
                    retry++;
                }
            }

            // Not reporting notification while we investigate why this could happen
            // await _mediator.Publish(new ImproperBehaviorNotification("Error initializing search parameters"), cancellationToken);
        }

        private bool TryGetFromTypeLookup(string resourceType, string code, out SearchParameterInfo searchParameter)
        {
            searchParameter = null;

            if (!TypeLookup.TryGetValue(resourceType, out var lookup) ||
                !lookup.TryGetValue(code, out var queue))
            {
                return false;
            }

            searchParameter = queue
                .Where(uri => UrlLookup.ContainsKey(uri))
                .Select(uri => UrlLookup[uri])
                .FirstOrDefault();

            return searchParameter != null;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859:Use concrete types when possible for improved performance", Justification = "Collection defined on model")]
        private ICollection<string> GetDerivedResourceTypes(IReadOnlyCollection<string> resourceTypes)
        {
            return GetDerivedResourceTypes(_modelInfoProvider, resourceTypes);
        }

        public static ICollection<string> GetDerivedResourceTypes(IModelInfoProvider modelInfoProvider,  IReadOnlyCollection<string> resourceTypes)
        {
            resourceTypes ??= []; // treat null list as empty

            var completeResourceList = new HashSet<string>(resourceTypes);

            foreach (var baseResourceType in resourceTypes)
            {
                if (baseResourceType == KnownResourceTypes.Resource)
                {
                    completeResourceList.UnionWith(modelInfoProvider.GetResourceTypeNames().ToHashSet());

                    // We added all possible resource types, so no need to continue
                    break;
                }

                if (baseResourceType == KnownResourceTypes.DomainResource)
                {
                    var domainResourceChildResourceTypes = modelInfoProvider.GetResourceTypeNames().ToHashSet();

                    // Remove types that inherit from Resource directly
                    domainResourceChildResourceTypes.Remove(KnownResourceTypes.Binary);
                    domainResourceChildResourceTypes.Remove(KnownResourceTypes.Bundle);
                    domainResourceChildResourceTypes.Remove(KnownResourceTypes.Parameters);

                    completeResourceList.UnionWith(domainResourceChildResourceTypes);
                }
            }

            return completeResourceList;
        }

        public async Task<bool> GetAndApplySearchParameterUpdates(CancellationToken cancellationToken = default, bool zeroWaitForSemaphore = false)
        {
            if (!await _refreshSemaphore.WaitAsync(TimeSpan.FromSeconds(zeroWaitForSemaphore ? 0 : _maxSecondsToWaitForSemaphore), cancellationToken))
            {
                var msg = $"Could not acquire lock to refresh search parameter cache after waiting for {_maxSecondsToWaitForSemaphore} seconds.";
                if (zeroWaitForSemaphore)
                {
                    _logger.LogInformation(msg);
                    return false;
                }
                else
                {
                    _logger.LogError(msg);
                    throw new InvalidOperationException(msg);
                }
            }

            try
            {
                var results = await GetSearchParameterStatusUpdates(cancellationToken, _searchParamLastUpdated);
                var statuses = results.Statuses;

                // First process any deletes or disables, then we will do any adds or updates
                // this way any deleted or params which might have the same code or name as a new
                // parameter will not cause conflicts. Disabled params just need to be removed when calculating the hash.
                foreach (var searchParam in statuses.Where(p => p.Status == SearchParameterStatus.Deleted))
                {
                    DeleteSearchParameterIfExists(searchParam.Uri.OriginalString);
                }

                foreach (var searchParam in statuses.Where(p => p.Status == SearchParameterStatus.PendingDelete))
                {
                    UpdateSearchParameterStatus(searchParam.Uri.OriginalString, SearchParameterStatus.PendingDelete);
                }

                // Identify all System Defined Search Parameters and filter them from statuses
                var systemDefinedSearchParameterUris = new HashSet<string>(
                    AllSearchParameters
                        .Where(p => p.IsSystemDefined)
                        .Select(p => p.Url.OriginalString),
                    StringComparer.Ordinal);

                var statusesToFetch = statuses
                    .Where(p => p.Status == SearchParameterStatus.Enabled || p.Status == SearchParameterStatus.Supported)
                    .Where(p => !systemDefinedSearchParameterUris.Contains(p.Uri.OriginalString)).ToList();

                // Batch fetch all SearchParameter resources in one call
                var searchParamResources = await GetSearchParametersByUrls(
                    statusesToFetch
                        .Select(p => p.Uri.OriginalString)
                        .ToList(),
                    cancellationToken);

                var paramsToAdd = new List<ITypedElement>();
                var allHaveResources = true;
                foreach (var searchParam in statusesToFetch)
                {
                    if (!searchParamResources.TryGetValue(searchParam.Uri.OriginalString, out var searchParamResource))
                    {
                        _logger.LogInformation(
                            "Updated SearchParameter status found for SearchParameter: {Url}, but did not find any SearchParameter resources when querying for this url.",
                            searchParam.Uri);

                        if (searchParam.LastUpdated > DateTimeOffset.UtcNow.AddMinutes(-10)) // same as for in cache
                        {
                            allHaveResources = false;
                        }

                        continue;
                    }

                    // check if search param is in cache and add if does not exist
                    if (TryGetSearchParameter(searchParam.Uri.OriginalString, out var existingSearchParam))
                    {
                        // if the previous version of the search parameter exists we should delete the old information currently stored
                        DeleteSearchParameter(searchParam.Uri.OriginalString);
                    }

                    paramsToAdd.Add(searchParamResource);

                    // Add parameters incrementally per chunk to reduce peak memory footprint
                    if (paramsToAdd.Count >= 100)
                    {
                        AddNewSearchParameters(paramsToAdd);
                        paramsToAdd.Clear();
                    }
                }

                // Add any remaining parameters
                if (paramsToAdd.Any())
                {
                    AddNewSearchParameters(paramsToAdd);
                }

                // Once added to the definition manager we can update their status
                await ApplySearchParameterStatus(statuses, cancellationToken);

                var inCache = ParametersAreInCache(statusesToFetch, cancellationToken);

                // If cache is updated directly and not from the database not all will have corresponding resources.
                // Do not advance or log the timestamp unless the cache contents are conclusive for this cycle.
                if (inCache && allHaveResources && results.LastUpdated.HasValue)
                {
                    _searchParamLastUpdated = results.LastUpdated.Value; // this should be the only place in the code to assign last updated
                }

                if (_searchParamLastUpdated.HasValue)
                {
                    // Log to EventLog for cross-instance convergence tracking (SQL only; Cosmos/File are no-ops).
                    var lastUpdatedText = _searchParamLastUpdated.Value.ToString("yyyy-MM-dd HH:mm:ss.fffffff");
                    await _statusDataStore.TryLogEvent(_statusDataStore.SearchParamCacheUpdateProcessName, "Warn", lastUpdatedText, null, cancellationToken);
                }
            }
            finally
            {
                try
                {
                    _refreshSemaphore.Release();
                }
                catch (ObjectDisposedException)
                {
                    // Expected during host shutdown when Dispose() races with an in-flight async callback.
                }
            }

            return true;
        }

        private async Task ApplySearchParameterStatus(IReadOnlyCollection<ResourceSearchParameterStatus> updatedSearchParameterStatus, CancellationToken cancellationToken)
        {
            if (!updatedSearchParameterStatus.Any())
            {
                _logger.LogDebug("ApplySearchParameterStatus: No search parameter status updates to apply.");
                return;
            }

            var updated = new List<SearchParameterInfo>();

            foreach (var paramStatus in updatedSearchParameterStatus)
            {
                if (TryGetSearchParameter(paramStatus.Uri.OriginalString, out var param))
                {
                    var tempStatus = EvaluateSearchParamStatus(paramStatus);

                    param.IsSearchable = tempStatus.IsSearchable;
                    param.IsSupported = tempStatus.IsSupported;
                    param.IsPartiallySupported = tempStatus.IsPartiallySupported;
                    param.SortStatus = paramStatus.SortStatus;
                    param.SearchParameterStatus = paramStatus.Status;
                    updated.Add(param);
                }
                else if (!updatedSearchParameterStatus.Any(p => p.Uri.Equals(paramStatus.Uri) && (p.Status == SearchParameterStatus.Deleted || p.Status == SearchParameterStatus.Disabled)))
                {
                    // if we cannot find the search parameter in the search parameter definition manager
                    // and there is an entry in the list of updates with a delete status then it indicates
                    // the search parameter was deleted before it was added to this instance, and there is no issue
                    // however if there is no indication that the search parameter was deleted, then there is a problem
                    _logger.LogError(Core.Resources.UnableToUpdateSearchParameter, paramStatus.Uri);
                }
            }

            _statusDataStore.SyncStatuses(updatedSearchParameterStatus);

            _logger.LogDebug("ApplySearchParameterStatus: Synced params. Updated cache timestamp.");
            await _mediator.Publish(new SearchParametersUpdatedNotification(updated), cancellationToken);
        }

        private async Task<(IReadOnlyCollection<ResourceSearchParameterStatus> Statuses, DateTimeOffset? LastUpdated)> GetSearchParameterStatusUpdates(CancellationToken cancellationToken, DateTimeOffset? startLastUpdated = null)
        {
            var searchParamStatuses = await _statusDataStore.GetSearchParameterStatuses(cancellationToken, startLastUpdated);
            var lastUpdated = searchParamStatuses.Any() ? searchParamStatuses.Max(_ => _.LastUpdated) : (DateTimeOffset?)null;
            return (searchParamStatuses, lastUpdated);
        }

        // This should handle racing condition between saving new parameter on one VM and refreshing cache on the other,
        // when refresh is invoked between saving status and saving resource.
        // This will not be needed when order of saves is reversed (resource first, then status)
        private bool ParametersAreInCache(IReadOnlyCollection<ResourceSearchParameterStatus> statuses, CancellationToken cancellationToken)
        {
            var inCache = true;
            foreach (var status in statuses)
            {
                TryGetSearchParameter(status.Uri.OriginalString, out var existingSearchParam);
                if (existingSearchParam == null)
                {
                    var msg = $"Did not find in cache uri={status.Uri.OriginalString} status={status.Status}";
                    _logger.LogInformation(msg);

                    // if the parameter was updated in the last 10 minutes it's possible we hit race condition
                    // where status was updated but resource is not yet saved, so we should not consider this as cache miss
                    if (status.LastUpdated > DateTimeOffset.UtcNow.AddMinutes(-10))
                    {
                        inCache = false;
                    }
                }
            }

            return inCache;
        }

        private async Task<Dictionary<string, ITypedElement>> GetSearchParametersByUrls(List<string> urls, CancellationToken cancellationToken)
        {
            if (!urls.Any())
            {
                return new Dictionary<string, ITypedElement>();
            }

            const int chunkSize = 100;
            var searchParametersByUrl = new Dictionary<string, ITypedElement>(StringComparer.Ordinal);
            var unresolvedUrls = new HashSet<string>(urls, StringComparer.Ordinal);

            using IScoped<ISearchService> search = _searchServiceFactory.Invoke();

            string continuationToken = null;

            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                var queryParams = new List<Tuple<string, string>>
                {
                    Tuple.Create(KnownQueryParameterNames.Count, chunkSize.ToString()),
                };

                if (!string.IsNullOrEmpty(continuationToken))
                {
                    queryParams.Add(
                        Tuple.Create(
                            KnownQueryParameterNames.ContinuationToken,
                            ContinuationTokenEncoder.Encode(continuationToken)));
                }

                var result = await search.Value.SearchAsync(KnownResourceTypes.SearchParameter, queryParams, cancellationToken);
                if (result?.Results != null)
                {
                    foreach (var entry in result.Results)
                    {
                        var typedElement = entry.Resource?.RawResource?.ToITypedElement(_modelInfoProvider);
                        if (typedElement == null)
                        {
                            continue;
                        }

                        var url = typedElement.GetStringScalar("url");
                        if (!string.IsNullOrEmpty(url) && unresolvedUrls.Remove(url))
                        {
                            searchParametersByUrl[url] = typedElement;

                            if (unresolvedUrls.Count == 0)
                            {
                                return searchParametersByUrl;
                            }
                        }
                    }
                }

                continuationToken = result?.ContinuationToken;
            }
            while (!string.IsNullOrEmpty(continuationToken));

            if (unresolvedUrls.Count > 0)
            {
                _logger.LogWarning(
                    "Could not resolve {Count} SearchParameter URL(s). Samples: {Urls}",
                    unresolvedUrls.Count,
                    string.Join(", ", unresolvedUrls.Take(10)));
            }

            return searchParametersByUrl;
        }

        private static TempStatus EvaluateSearchParamStatus(ResourceSearchParameterStatus paramStatus)
        {
            TempStatus tempStatus;
            tempStatus.IsSearchable = paramStatus.Status == SearchParameterStatus.Enabled;
            tempStatus.IsSupported = paramStatus.Status == SearchParameterStatus.Supported || paramStatus.Status == SearchParameterStatus.Enabled;
            tempStatus.IsPartiallySupported = paramStatus.IsPartiallySupported;

            return tempStatus;
        }

        private async Task SetStatesOfKnownParameters(CancellationToken cancellationToken)
        {
            var updated = new List<SearchParameterInfo>();
            var searchParamResourceStatus = await _statusDataStore.GetSearchParameterStatuses(cancellationToken);
            var parameters = searchParamResourceStatus.ToDictionary(x => x.Uri?.OriginalString, StringComparer.Ordinal);

            // Set states of known parameters
            foreach (SearchParameterInfo p in AllSearchParameters)
            {
                if (parameters.TryGetValue(p.Url?.OriginalString, out ResourceSearchParameterStatus result))
                {
                    var tempStatus = EvaluateSearchParamStatus(result);

                    if (result.Status == SearchParameterStatus.Unsupported)
                    {
                        // Re-check if this parameter is now supported.
                        (bool Supported, bool IsPartiallySupported) supportedResult = CheckSearchParameterSupport(p);
                        tempStatus.IsSupported = supportedResult.Supported;
                        tempStatus.IsPartiallySupported = supportedResult.IsPartiallySupported;
                    }

                    if (p.IsSearchable != tempStatus.IsSearchable ||
                        p.IsSupported != tempStatus.IsSupported ||
                        p.IsPartiallySupported != tempStatus.IsPartiallySupported ||
                        p.SortStatus != result.SortStatus ||
                        p.SearchParameterStatus != result.Status)
                    {
                        p.IsSearchable = tempStatus.IsSearchable;
                        p.IsSupported = tempStatus.IsSupported;
                        p.IsPartiallySupported = tempStatus.IsPartiallySupported;
                        p.SortStatus = result.SortStatus;
                        p.SearchParameterStatus = result.Status;

                        updated.Add(p);
                    }
                }
                else
                {
                    // ResourceTypeSearchParameter is a special hardcoded parameter added to
                    // AllSearchParameters by the UrlLookup registration. It has no entry in the
                    // status store and SearchParameterSupportResolver.IsSearchParameterSupported
                    // throws "No target resources defined" for it because it has no BaseResourceTypes
                    // or TargetResourceTypes. Force it to searchable/supported so background tasks
                    // (which use SearchableSearchParameterDefinitionManager with UsePartialSearchParams=false)
                    // don't throw SearchParameterNotSupportedException.
                    if (p.Url == SearchParameterNames.ResourceTypeUri)
                    {
                        p.IsSearchable = true;
                        p.IsSupported = true;
                        updated.Add(p);
                        continue;
                    }

                    p.IsSearchable = false;

                    // Check if this parameter is now supported.
                    (bool Supported, bool IsPartiallySupported) supportedResult = CheckSearchParameterSupport(p);
                    p.IsSupported = supportedResult.Supported;
                    p.IsPartiallySupported = supportedResult.IsPartiallySupported;

                    updated.Add(p);
                }
            }

            var disableSortIndicesList = AllSearchParameters.Where(u => _enabledSortIndices.Contains(u.Url.ToString()) && u.SortStatus != SortParameterStatus.Enabled);
            if (disableSortIndicesList.Any())
            {
                _logger.LogError("SearchParameterDefinitionManager: Sort status is not enabled {Environment.NewLine} {Message}", Environment.NewLine, string.Join($"{Environment.NewLine}    ", disableSortIndicesList.Select(u => "Url : " + u.Url.ToString() + ", Sort status : " + u.SortStatus.ToString())));
            }

            await _mediator.Publish(new SearchParametersUpdatedNotification(updated), cancellationToken);
            await _mediator.Publish(new SearchParametersInitializedNotification(), cancellationToken);
        }

        private (bool Supported, bool IsPartiallySupported) CheckSearchParameterSupport(SearchParameterInfo parameterInfo)
        {
            try
            {
                return _searchParameterSupportResolver.IsSearchParameterSupported(parameterInfo);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Unable to resolve search parameter {Code}. Exception: {Exception}", parameterInfo?.Code, ex);
                return (false, false);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _refreshSemaphore?.Dispose();
            }
        }

        private struct TempStatus
        {
            public bool IsSearchable;
            public bool IsSupported;
            public bool IsPartiallySupported;
        }
    }
}
