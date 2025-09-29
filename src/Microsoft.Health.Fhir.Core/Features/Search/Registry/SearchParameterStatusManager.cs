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
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Registry
{
    public class SearchParameterStatusManager : INotificationHandler<SearchParameterDefinitionManagerInitialized>, ISearchParameterStatusManager
    {
        private readonly ISearchParameterStatusDataStore _searchParameterStatusDataStore;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly ISearchParameterSupportResolver _searchParameterSupportResolver;
        private readonly IMediator _mediator;
        private readonly ILogger<SearchParameterStatusManager> _logger;
        private DateTimeOffset _latestSearchParams;
        private readonly List<string> enabledSortIndices = new List<string>() { "http://hl7.org/fhir/SearchParameter/individual-birthdate", "http://hl7.org/fhir/SearchParameter/individual-family", "http://hl7.org/fhir/SearchParameter/individual-given" };

        public SearchParameterStatusManager(
            ISearchParameterStatusDataStore searchParameterStatusDataStore,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            ISearchParameterSupportResolver searchParameterSupportResolver,
            IMediator mediator,
            ILogger<SearchParameterStatusManager> logger)
        {
            EnsureArg.IsNotNull(searchParameterStatusDataStore, nameof(searchParameterStatusDataStore));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(searchParameterSupportResolver, nameof(searchParameterSupportResolver));
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _searchParameterStatusDataStore = searchParameterStatusDataStore;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _searchParameterSupportResolver = searchParameterSupportResolver;
            _mediator = mediator;
            _logger = logger;

            _latestSearchParams = DateTimeOffset.MinValue;
        }

        /// <summary>
        /// Ensures the search parameter cache is fresh by validating against the database max LastUpdated timestamp.
        /// Uses configurable time-based intervals to balance freshness with performance.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if cache was stale and needs full refresh, false if cache is up to date</returns>
        public async Task<bool> EnsureCacheFreshnessAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                // Get max LastUpdated from database efficiently
                var maxDbLastUpdated = await _searchParameterStatusDataStore.GetMaxLastUpdatedAsync(cancellationToken);

                // Check if our cache is stale
                if (maxDbLastUpdated > _latestSearchParams)
                {
                    _logger.LogInformation(
                        "Search parameter cache is stale. Cache timestamp: {CacheTimestamp}, Database max: {DbMaxTimestamp}. Cache refresh needed.",
                        _latestSearchParams,
                        maxDbLastUpdated);

                    return true; // Cache is stale - caller should perform full refresh
                }
                else
                {
                    _logger.LogDebug(
                        "Search parameter cache is up to date. Cache timestamp: {CacheTimestamp}, Database max: {DbMaxTimestamp}.",
                        _latestSearchParams,
                        maxDbLastUpdated);

                    return false; // Cache is fresh - no action needed
                }
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "SearchParameter cache refresh was canceled. Will retry on next scheduled interval.");
                throw;
            }
            catch (TimeoutException ex)
            {
                _logger.LogWarning(ex, "Timeout Exception during search parameter cache validation. Assuming cache is stale to trigger refresh.");

                // When in doubt, assume cache is stale - better to do unnecessary work than miss updates
                return true;
            }
        }

        internal async Task EnsureInitializedAsync(CancellationToken cancellationToken)
        {
            var updated = new List<SearchParameterInfo>();
            var searchParamResourceStatus = await _searchParameterStatusDataStore.GetSearchParameterStatuses(cancellationToken);
            var parameters = searchParamResourceStatus.ToDictionary(x => x.Uri);
            _latestSearchParams = parameters.Values.Select(p => p.LastUpdated).Max();

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
                _logger.LogError("SearchParameterStatusManager: Sort status is not enabled {Environment.NewLine} {Message}", Environment.NewLine, string.Join($"{Environment.NewLine}    ", disableSortIndicesList.Select(u => "Url : " + u.Url.ToString() + ", Sort status : " + u.SortStatus.ToString())));
            }

            await _mediator.Publish(new SearchParametersUpdatedNotification(updated), cancellationToken);
            await _mediator.Publish(new SearchParametersInitializedNotification(), cancellationToken);
        }

        public async Task Handle(SearchParameterDefinitionManagerInitialized notification, CancellationToken cancellationToken)
        {
            _logger.LogInformation("SearchParameterStatusManager: Search parameter definition manager initialized");
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
            var parameters = (await _searchParameterStatusDataStore.GetSearchParameterStatuses(cancellationToken))
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
                        });
                    }
                }
                catch (SearchParameterNotSupportedException ex)
                {
                    _logger.LogError(ex, "The search parameter '{Uri}' not supported.", uri);

                    // Note: SearchParameterNotSupportedException can be thrown by SearchParameterDefinitionManager.GetSearchParameter
                    // when the given url is not found in its cache that can happen when the cache becomes out of sync with the store.
                    // Use this flag to ignore the exception and continue the update process for the rest of search parameters.
                    // (e.g. $bulk-delete ensuring deletion of as many search parameters as possible.)
                    if (!ignoreSearchParameterNotSupportedException)
                    {
                        throw;
                    }
                }
            }

            await _searchParameterStatusDataStore.UpsertStatuses(searchParameterStatusList, cancellationToken);

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
        }

        public async Task<IReadOnlyCollection<ResourceSearchParameterStatus>> GetSearchParameterStatusUpdates(CancellationToken cancellationToken)
        {
            var searchParamStatus = await _searchParameterStatusDataStore.GetSearchParameterStatuses(cancellationToken);
            return searchParamStatus.Where(p => p.LastUpdated > _latestSearchParams).ToList();
        }

        public async Task<IReadOnlyCollection<ResourceSearchParameterStatus>> GetAllSearchParameterStatus(CancellationToken cancellationToken)
        {
            return await _searchParameterStatusDataStore.GetSearchParameterStatuses(cancellationToken);
        }

        /// <summary>
        /// Used to apply search parameter status updates to the SearchParameterDefinitionManager.Used in reindex operation when checking every 10 minutes or so.
        /// </summary>
        /// <param name="updatedSearchParameterStatus">Collection of updated search parameter statuses</param>
        /// <param name="cancellationToken">Cancellation Token</param>
        public async Task ApplySearchParameterStatus(IReadOnlyCollection<ResourceSearchParameterStatus> updatedSearchParameterStatus, CancellationToken cancellationToken)
        {
            if (!updatedSearchParameterStatus.Any())
            {
                // Even when there are no updates to apply, we need to update our cache timestamp
                // to reflect that we've successfully synchronized with the database
                await UpdateCacheTimestampAsync(cancellationToken);
                _logger.LogDebug("ApplySearchParameterStatus: No search parameter status updates to apply. Updated cache timestamp.");
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
                    // if we cannot find the search parameter in the search parameter definition manager
                    // and there is an entry in the list of updates with a delete status then it indicates
                    // the search parameter was deleted before it was added to this instance, and there is no issue
                    // however if there is no indication that the search parameter was deleted, then there is a problem
                    _logger.LogError(Core.Resources.UnableToUpdateSearchParameter, paramStatus.Uri);
                }
            }

            _searchParameterStatusDataStore.SyncStatuses(updatedSearchParameterStatus);

            await UpdateCacheTimestampAsync(cancellationToken);

            await _mediator.Publish(new SearchParametersUpdatedNotification(updated), cancellationToken);
        }

        /// <summary>
        /// Updates the cache timestamp to indicate that a full cache refresh has been completed successfully.
        /// This prevents unnecessary cache refreshes when no new updates are available.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        private async Task UpdateCacheTimestampAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                // Get the current max LastUpdated from database and update our cache timestamp
                var maxDbLastUpdated = await _searchParameterStatusDataStore.GetMaxLastUpdatedAsync(cancellationToken);
                _latestSearchParams = maxDbLastUpdated;

                _logger.LogDebug(
                    "Updated SearchParameter cache timestamp to {CacheTimestamp} after successful refresh.",
                    _latestSearchParams);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update cache timestamp after successful refresh. This may cause unnecessary cache refreshes.");

                // Don't throw - this is not critical for functionality
            }
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
