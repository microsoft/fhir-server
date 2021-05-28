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
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Registry
{
    public class SearchParameterStatusManager : INotificationHandler<SearchParameterDefinitionManagerInitialized>
    {
        private readonly ISearchParameterStatusDataStore _searchParameterStatusDataStore;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly ISearchParameterSupportResolver _searchParameterSupportResolver;
        private readonly IMediator _mediator;
        private readonly ILogger<SearchParameterStatusManager> _logger;
        private DateTimeOffset _latestSearchParams;

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

        internal async Task EnsureInitializedAsync(CancellationToken cancellationToken)
        {
            var updated = new List<SearchParameterInfo>();
            var searchParamResourceStatus = await _searchParameterStatusDataStore.GetSearchParameterStatuses();
            var parameters = searchParamResourceStatus.ToDictionary(x => x.Uri);
            _latestSearchParams = parameters.Values.Select(p => p.LastUpdated).Max();

            // Set states of known parameters
            foreach (SearchParameterInfo p in _searchParameterDefinitionManager.AllSearchParameters)
            {
                if (parameters.TryGetValue(p.Url, out ResourceSearchParameterStatus result))
                {
                    var tempStatus = EvaluateSearchParamStatus(result);

                    if (result.Status == SearchParameterStatus.Disabled)
                    {
                        // Re-check if this parameter is now supported.
                        (bool Supported, bool IsPartiallySupported) supportedResult = CheckSearchParameterSupport(p);
                        tempStatus.IsSupported = supportedResult.Supported;
                        tempStatus.IsPartiallySupported = supportedResult.IsPartiallySupported;
                    }

                    if (p.IsSearchable != tempStatus.IsSearchable ||
                        p.IsSupported != tempStatus.IsSupported ||
                        p.IsPartiallySupported != tempStatus.IsPartiallySupported ||
                        p.SortStatus != result.SortStatus)
                    {
                        p.IsSearchable = tempStatus.IsSearchable;
                        p.IsSupported = tempStatus.IsSupported;
                        p.IsPartiallySupported = tempStatus.IsPartiallySupported;
                        p.SortStatus = result.SortStatus;

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

            await _mediator.Publish(new SearchParametersUpdatedNotification(updated), cancellationToken);
            await _mediator.Publish(new SearchParametersInitializedNotification(), cancellationToken);
        }

        public async Task Handle(SearchParameterDefinitionManagerInitialized notification, CancellationToken cancellationToken)
        {
            await EnsureInitializedAsync(cancellationToken);
        }

        public async Task UpdateSearchParameterStatusAsync(IReadOnlyCollection<string> searchParameterUris, SearchParameterStatus status)
        {
            var searchParameterStatusList = new List<ResourceSearchParameterStatus>();
            var updated = new List<SearchParameterInfo>();
            var parameters = (await _searchParameterStatusDataStore.GetSearchParameterStatuses())
                .ToDictionary(x => x.Uri);

            foreach (string uri in searchParameterUris)
            {
                var searchParamUri = new Uri(uri);

                SearchParameterInfo paramInfo = _searchParameterDefinitionManager.GetSearchParameter(searchParamUri);
                updated.Add(paramInfo);
                paramInfo.IsSearchable = status == SearchParameterStatus.Enabled;
                paramInfo.IsSupported = status == SearchParameterStatus.Supported || status == SearchParameterStatus.Enabled;

                if (parameters.TryGetValue(searchParamUri, out var existingStatus))
                {
                    existingStatus.LastUpdated = Clock.UtcNow;
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
                        LastUpdated = Clock.UtcNow,
                        Status = status,
                        Uri = searchParamUri,
                    });
                }
            }

            await _searchParameterStatusDataStore.UpsertStatuses(searchParameterStatusList);

            await _mediator.Publish(new SearchParametersUpdatedNotification(updated));
        }

        internal async Task AddSearchParameterStatusAsync(IReadOnlyCollection<string> searchParamUris)
        {
            // new search parameters are added as supported, until reindexing occurs, when
            // they will be fully enabled
            await UpdateSearchParameterStatusAsync(searchParamUris, SearchParameterStatus.Supported);
        }

        internal async Task DeleteSearchParameterStatusAsync(string url)
        {
            var searchParamUris = new List<string>() { url };
            await UpdateSearchParameterStatusAsync(searchParamUris, SearchParameterStatus.Deleted);
        }

        internal async Task<IReadOnlyCollection<ResourceSearchParameterStatus>> GetSearchParameterStatusUpdates()
        {
            var searchParamStatus = await _searchParameterStatusDataStore.GetSearchParameterStatuses();
            return searchParamStatus.Where(p => p.LastUpdated > _latestSearchParams).ToList();
        }

        internal async Task ApplySearchParameterStatus(IReadOnlyCollection<ResourceSearchParameterStatus> updatedSearchParameterStatus, CancellationToken cancellationToken)
        {
            var updated = new List<SearchParameterInfo>();

            foreach (var paramStatus in updatedSearchParameterStatus)
            {
                var param = _searchParameterDefinitionManager.GetSearchParameter(paramStatus.Uri);

                var tempStatus = EvaluateSearchParamStatus(paramStatus);

                param.IsSearchable = tempStatus.IsSearchable;
                param.IsSupported = tempStatus.IsSupported;
                param.IsPartiallySupported = tempStatus.IsPartiallySupported;
                param.SortStatus = paramStatus.SortStatus;

                updated.Add(param);
            }

            _latestSearchParams = updatedSearchParameterStatus.Select(p => p.LastUpdated).Max();

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
                _logger.LogWarning("Unable to resolve search parameter {0}. Exception: {1}", parameterInfo?.Code, ex);
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
