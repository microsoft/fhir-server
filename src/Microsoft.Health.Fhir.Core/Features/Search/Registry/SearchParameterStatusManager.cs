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
        }

        internal async Task EnsureInitializedAsync(CancellationToken cancellationToken)
        {
            var updated = new List<SearchParameterInfo>();
            var searchParamResourceStatus = await _searchParameterStatusDataStore.GetSearchParameterStatuses(cancellationToken);
            var parameters = searchParamResourceStatus.ToDictionary(x => x.Uri);

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

        public async Task<(IReadOnlyCollection<ResourceSearchParameterStatus> Statuses, DateTimeOffset? LastUpdated)> GetSearchParameterStatusUpdates(CancellationToken cancellationToken, DateTimeOffset? startLastUpdated = null)
        {
            var searchParamStatuses = await _searchParameterStatusDataStore.GetSearchParameterStatuses(cancellationToken, startLastUpdated);
            var lastUpdated = searchParamStatuses.Any() ? searchParamStatuses.Max(_ => _.LastUpdated) : (DateTimeOffset?)null;
            return (searchParamStatuses, lastUpdated);
        }

        // This is just a conveniance wrapper method that should not be going to store directly
        public async Task<IReadOnlyCollection<ResourceSearchParameterStatus>> GetAllSearchParameterStatus(CancellationToken cancellationToken)
        {
            return (await GetSearchParameterStatusUpdates(cancellationToken)).Statuses;
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
                _logger.LogDebug("ApplySearchParameterStatus: No search parameter status updates to apply.");
                return;
            }

            var updated = new List<SearchParameterInfo>();

            foreach (var paramStatus in updatedSearchParameterStatus)
            {
                var tempStatus = EvaluateSearchParamStatus(paramStatus);

                // Use ApplyStatusToAllMatchingObjects to update both UrlLookup and TypeLookup.
                // This is necessary because TypeLookup may contain different object instances
                // due to inheritance caching in BuildSearchParameterDefinition.
                if (_searchParameterDefinitionManager.ApplyStatusToAllMatchingObjects(
                    paramStatus.Uri.OriginalString,
                    tempStatus.IsSearchable,
                    tempStatus.IsSupported,
                    tempStatus.IsPartiallySupported,
                    paramStatus.SortStatus,
                    paramStatus.Status))
                {
                    // Get the param for the notification list
                    if (_searchParameterDefinitionManager.TryGetSearchParameter(paramStatus.Uri.OriginalString, out var param))
                    {
                        updated.Add(param);
                    }
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

            _logger.LogDebug("ApplySearchParameterStatus: Synced params. Updated cache timestamp.");
            await _mediator.Publish(new SearchParametersUpdatedNotification(updated), cancellationToken);
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
