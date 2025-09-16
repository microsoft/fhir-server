// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Caching;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search.Caching;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Registry
{
    /// <summary>
    /// Redis-enabled search parameter status manager that uses distributed caching
    /// for improved multi-instance consistency and performance.
    /// This implementation prioritizes Redis cache over database for better performance.
    /// </summary>
    public class RedisSearchParameterStatusManager : INotificationHandler<SearchParameterDefinitionManagerInitialized>, ISearchParameterStatusManager
    {
        private readonly ISearchParameterStatusDataStore _searchParameterStatusDataStore;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly ISearchParameterSupportResolver _searchParameterSupportResolver;
        private readonly ISearchParameterCache _distributedCache;
        private readonly IMediator _mediator;
        private readonly ILogger<RedisSearchParameterStatusManager> _logger;

        private DateTimeOffset _latestSearchParams;
        private DateTimeOffset _lastDistributedCacheCheck;
        private readonly List<string> enabledSortIndices = new List<string>()
        {
            "http://hl7.org/fhir/SearchParameter/individual-birthdate",
            "http://hl7.org/fhir/SearchParameter/individual-family",
            "http://hl7.org/fhir/SearchParameter/individual-given",
        };

        public RedisSearchParameterStatusManager(
            ISearchParameterStatusDataStore searchParameterStatusDataStore,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            ISearchParameterSupportResolver searchParameterSupportResolver,
            ISearchParameterCache distributedCache,
            IMediator mediator,
            ILogger<RedisSearchParameterStatusManager> logger)
        {
            EnsureArg.IsNotNull(searchParameterStatusDataStore, nameof(searchParameterStatusDataStore));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(searchParameterSupportResolver, nameof(searchParameterSupportResolver));
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _searchParameterStatusDataStore = searchParameterStatusDataStore;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _searchParameterSupportResolver = searchParameterSupportResolver;
            _distributedCache = distributedCache;
            _mediator = mediator;
            _logger = logger;

            _latestSearchParams = DateTimeOffset.MinValue;
            _lastDistributedCacheCheck = DateTimeOffset.MinValue;
        }

        internal async Task EnsureInitializedAsync(CancellationToken cancellationToken)
        {
            var updated = new List<SearchParameterInfo>();
            IReadOnlyCollection<ResourceSearchParameterStatus> searchParamResourceStatus;

            // Try Redis cache first, then fallback to database
            try
            {
                _logger.LogInformation("Attempting to load search parameter statuses from Redis cache");
                searchParamResourceStatus = await _distributedCache.GetAllAsync(cancellationToken);

                if (!searchParamResourceStatus.Any())
                {
                    _logger.LogInformation("No search parameter statuses found in Redis cache, loading from data store");
                    searchParamResourceStatus = await _searchParameterStatusDataStore.GetSearchParameterStatuses(cancellationToken);

                    // Populate Redis cache for future use
                    if (searchParamResourceStatus.Any())
                    {
                        await _distributedCache.SetAsync(searchParamResourceStatus, cancellationToken);
                        _logger.LogInformation("Cached {Count} search parameter statuses in Redis", searchParamResourceStatus.Count);
                    }
                }
                else
                {
                    _logger.LogInformation("Loaded {Count} search parameter statuses from Redis cache", searchParamResourceStatus.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load search parameter statuses from Redis cache, falling back to data store");
                searchParamResourceStatus = await _searchParameterStatusDataStore.GetSearchParameterStatuses(cancellationToken);
            }

            var parameters = searchParamResourceStatus.ToDictionary(x => x.Uri);
            _latestSearchParams = parameters.Values.Any() ? parameters.Values.Select(p => p.LastUpdated).Max() : DateTimeOffset.UtcNow;

            EnsureArg.IsNotNull(_searchParameterDefinitionManager.AllSearchParameters);
            EnsureArg.IsTrue(_searchParameterDefinitionManager.AllSearchParameters.Any());
            EnsureArg.IsTrue(parameters.Any());

            // Set states of known parameters
            foreach (SearchParameterInfo p in _searchParameterDefinitionManager.AllSearchParameters)
            {
                if (parameters.TryGetValue(p.Url, out ResourceSearchParameterStatus result))
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
                    p.IsSearchable = false;

                    // Check if this parameter is now supported.
                    (bool Supported, bool IsPartiallySupported) supportedResult = CheckSearchParameterSupport(p);
                    p.IsSupported = supportedResult.Supported;
                    p.IsPartiallySupported = supportedResult.IsPartiallySupported;

                    updated.Add(p);
                }
            }

            var disableSortIndicesList = _searchParameterDefinitionManager.AllSearchParameters.Where(u => enabledSortIndices.Contains(u.Url.ToString()) && u.SortStatus != SortParameterStatus.Enabled);
            if (disableSortIndicesList.Any())
            {
                _logger.LogError("RedisSearchParameterStatusManager: Sort status is not enabled {Environment.NewLine} {Message}", Environment.NewLine, string.Join($"{Environment.NewLine}    ", disableSortIndicesList.Select(u => "Url : " + u.Url.ToString() + ", Sort status : " + u.SortStatus.ToString())));
            }

            await _mediator.Publish(new SearchParametersUpdatedNotification(updated), cancellationToken);
            await _mediator.Publish(new SearchParametersInitializedNotification(), cancellationToken);
        }

        public async Task Handle(SearchParameterDefinitionManagerInitialized notification, CancellationToken cancellationToken)
        {
            // Only initialize if Redis is enabled
            if (_distributedCache == null)
            {
                _logger.LogInformation("Redis cache is disabled, RedisSearchParameterStatusManager will not initialize");
                return;
            }

            _logger.LogInformation("RedisSearchParameterStatusManager: Search parameter definition manager initialized");
            await EnsureInitializedAsync(cancellationToken);
        }

        public async Task UpdateSearchParameterStatusAsync(IReadOnlyCollection<string> searchParameterUris, SearchParameterStatus status, CancellationToken cancellationToken, bool ignoreSearchParameterNotSupportedException = false)
        {
            EnsureArg.IsNotNull(searchParameterUris);

            if (searchParameterUris.Count == 0)
            {
                return;
            }

            var searchParameterStatusList = new List<ResourceSearchParameterStatus>();
            var updated = new List<SearchParameterInfo>();
            var parameters = (await GetSearchParameterStatusesAsync(cancellationToken))
                .ToDictionary(x => x.Uri.OriginalString);

            foreach (string uri in searchParameterUris)
            {
                _logger.LogInformation("Setting the search parameter status of '{Uri}' to '{NewStatus}'", uri, status.ToString());

                try
                {
                    SearchParameterInfo paramInfo = _searchParameterDefinitionManager.GetSearchParameter(uri);
                    updated.Add(paramInfo);
                    paramInfo.IsSearchable = status == SearchParameterStatus.Enabled;
                    paramInfo.IsSupported = status == SearchParameterStatus.Supported || status == SearchParameterStatus.Enabled;

                    if (parameters.TryGetValue(uri, out var existingStatus))
                    {
                        existingStatus.Status = status;
                        existingStatus.LastUpdated = DateTimeOffset.UtcNow;

                        if (paramInfo.IsSearchable && existingStatus.SortStatus == SortParameterStatus.Supported)
                        {
                            existingStatus.SortStatus = SortParameterStatus.Enabled;
                            paramInfo.SortStatus = SortParameterStatus.Enabled;
                        }

                        searchParameterStatusList.Add(existingStatus);
                    }
                    else
                    {
                        searchParameterStatusList.Add(new ResourceSearchParameterStatus
                        {
                            Status = status,
                            Uri = new Uri(uri),
                            LastUpdated = DateTimeOffset.UtcNow,
                        });
                    }
                }
                catch (SearchParameterNotSupportedException ex)
                {
                    _logger.LogError(ex, "The search parameter '{Uri}' not supported.", uri);

                    if (!ignoreSearchParameterNotSupportedException)
                    {
                        throw;
                    }
                }
            }

            // Update database first (source of truth)
            await _searchParameterStatusDataStore.UpsertStatuses(searchParameterStatusList, cancellationToken);

            // Then update Redis cache using upsert to merge with existing cache
            try
            {
                await _distributedCache.UpsertAsync(searchParameterStatusList, replaceAll: false, cancellationToken);
                _logger.LogDebug("Upserted {Count} search parameter statuses in Redis cache", searchParameterStatusList.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to upsert search parameter statuses in Redis cache");
            }

            await _mediator.Publish(new SearchParametersUpdatedNotification(updated), cancellationToken);
        }

        public async Task AddSearchParameterStatusAsync(IReadOnlyCollection<string> searchParamUris, CancellationToken cancellationToken)
        {
            // new search parameters are added as supported, until reindexing occurs, when
            // they will be fully enabled
            await UpdateSearchParameterStatusAsync(searchParamUris, SearchParameterStatus.Supported, cancellationToken);
        }

        public async Task DeleteSearchParameterStatusAsync(string url, CancellationToken cancellationToken)
        {
            var searchParamUris = new List<string>() { url };
            await UpdateSearchParameterStatusAsync(searchParamUris, SearchParameterStatus.Deleted, cancellationToken);

            // Remove from Redis cache
            try
            {
                await _distributedCache.RemoveAsync(searchParamUris, cancellationToken);
                _logger.LogDebug("Removed search parameter status from Redis cache: {Uri}", url);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to remove search parameter status from Redis cache: {Uri}", url);
            }
        }

        public async Task<IReadOnlyCollection<ResourceSearchParameterStatus>> GetSearchParameterStatusUpdates(CancellationToken cancellationToken)
        {
            // Check Redis cache first with throttling (every 30 seconds)
            if (ShouldCheckDistributedCache())
            {
                try
                {
                    var distributedCacheVersion = await _distributedCache.GetCacheVersionAsync(cancellationToken);
                    if (distributedCacheVersion.HasValue && distributedCacheVersion.Value > _latestSearchParams)
                    {
                        _logger.LogDebug("Found newer search parameters in Redis cache (version: {CacheVersion})", distributedCacheVersion.Value);
                        var updatedFromCache = await _distributedCache.GetUpdatedAsync(_latestSearchParams, cancellationToken);
                        if (updatedFromCache.Any())
                        {
                            _latestSearchParams = updatedFromCache.Select(p => p.LastUpdated).Max();
                            _lastDistributedCacheCheck = DateTimeOffset.UtcNow;
                            return updatedFromCache;
                        }
                    }

                    _lastDistributedCacheCheck = DateTimeOffset.UtcNow;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to check distributed cache for search parameter updates, falling back to data store");
                }
            }

            // Fallback to database
            var searchParamStatus = await _searchParameterStatusDataStore.GetSearchParameterStatuses(cancellationToken);
            return searchParamStatus.Where(p => p.LastUpdated > _latestSearchParams).ToList();
        }

        public async Task<IReadOnlyCollection<ResourceSearchParameterStatus>> GetAllSearchParameterStatus(CancellationToken cancellationToken)
        {
            return await GetSearchParameterStatusesAsync(cancellationToken);
        }

        public async Task ApplySearchParameterStatus(IReadOnlyCollection<ResourceSearchParameterStatus> updatedSearchParameterStatus, CancellationToken cancellationToken)
        {
            if (!updatedSearchParameterStatus.Any())
            {
                return;
            }

            var updated = new List<SearchParameterInfo>();

            foreach (var paramStatus in updatedSearchParameterStatus)
            {
                if (_searchParameterDefinitionManager.TryGetSearchParameter(paramStatus.Uri.OriginalString, out var param))
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
                    _logger.LogError(Core.Resources.UnableToUpdateSearchParameter, paramStatus.Uri);
                }
            }

            // _searchParameterStatusDataStore.SyncStatuses(updatedSearchParameterStatus);

            if (updatedSearchParameterStatus.Any())
            {
                _latestSearchParams = updatedSearchParameterStatus.Select(p => p.LastUpdated).Max();

                // Update Redis cache using upsert to merge with existing cache
                try
                {
                    await _distributedCache.UpsertAsync(updatedSearchParameterStatus, replaceAll: false, cancellationToken);
                    _logger.LogDebug("Applied {Count} search parameter status updates to Redis cache", updatedSearchParameterStatus.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to apply search parameter status updates to Redis cache");
                }
            }

            await _mediator.Publish(new SearchParametersUpdatedNotification(updated), cancellationToken);
        }

        private async Task<IReadOnlyCollection<ResourceSearchParameterStatus>> GetSearchParameterStatusesAsync(CancellationToken cancellationToken)
        {
            // Try Redis cache first
            try
            {
                // Check if cache is stale and refresh if needed
                var isCacheStale = await _distributedCache.IsCacheStaleAsync(cancellationToken);
                if (isCacheStale)
                {
                    _logger.LogInformation("Cache appears to be stale, refreshing from data store");
                    var freshStatuses = await _searchParameterStatusDataStore.GetSearchParameterStatuses(cancellationToken);
                    if (freshStatuses.Count > 0)
                    {
                        await _distributedCache.SetAsync(freshStatuses, cancellationToken);
                        _logger.LogInformation("Refreshed {Count} search parameter statuses in Redis cache", freshStatuses.Count);
                        return freshStatuses;
                    }
                }

                var cachedStatuses = await _distributedCache.GetAllAsync(cancellationToken);
                if (cachedStatuses.Any())
                {
                    return cachedStatuses;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to retrieve search parameter statuses from Redis cache, falling back to data store");
            }

            // Fallback to data store
            return await _searchParameterStatusDataStore.GetSearchParameterStatuses(cancellationToken);
        }

        private bool ShouldCheckDistributedCache()
        {
            // Check distributed cache every 30 seconds to balance performance and consistency
            return DateTimeOffset.UtcNow - _lastDistributedCacheCheck > TimeSpan.FromSeconds(30);
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

        private static TempStatus EvaluateSearchParamStatus(ResourceSearchParameterStatus paramStatus)
        {
            TempStatus tempStatus;
            tempStatus.IsSearchable = paramStatus.Status == SearchParameterStatus.Enabled;
            tempStatus.IsSupported = paramStatus.Status == SearchParameterStatus.Supported || paramStatus.Status == SearchParameterStatus.Enabled;
            tempStatus.IsPartiallySupported = paramStatus.IsPartiallySupported;

            return tempStatus;
        }

        private struct TempStatus
        {
            public bool IsSearchable;
            public bool IsSupported;
            public bool IsPartiallySupported;
        }
    }
}
