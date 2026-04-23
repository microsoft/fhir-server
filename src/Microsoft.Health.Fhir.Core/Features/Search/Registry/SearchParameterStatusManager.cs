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
    public class SearchParameterStatusManager : ISearchParameterStatusManager
    {
        private readonly ISearchParameterStatusDataStore _searchParameterStatusDataStore;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly ILogger<SearchParameterStatusManager> _logger;

        public SearchParameterStatusManager(
            ISearchParameterStatusDataStore searchParameterStatusDataStore,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            ILogger<SearchParameterStatusManager> logger)
        {
            EnsureArg.IsNotNull(searchParameterStatusDataStore, nameof(searchParameterStatusDataStore));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _searchParameterStatusDataStore = searchParameterStatusDataStore;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _logger = logger;
        }

        public async Task UpdateSearchParameterStatusAsync(IReadOnlyCollection<string> searchParameterUris, SearchParameterStatus status, CancellationToken cancellationToken, bool ignoreSearchParameterNotSupportedException = false)
        {
            EnsureArg.IsNotNull(searchParameterUris);

            if (searchParameterUris.Count == 0)
            {
                return;
            }

            var searchParameterStatusList = new List<ResourceSearchParameterStatus>();
            var parameters = (await _searchParameterStatusDataStore.GetSearchParameterStatuses(cancellationToken))
                .ToDictionary(x => x.Uri.OriginalString, StringComparer.Ordinal);

            foreach (string uri in searchParameterUris)
            {
                _logger.LogInformation("Setting the search parameter status of '{Uri}' to '{NewStatus}'", uri, status.ToString());

                // Validate that the search parameter exists in the definition manager
                _searchParameterDefinitionManager.GetSearchParameter(uri);

                if (parameters.TryGetValue(uri, out var existingStatus))
                {
                    existingStatus.Status = status;
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

            await _searchParameterStatusDataStore.UpsertStatuses(searchParameterStatusList, cancellationToken);
        }

        public async Task<(IReadOnlyCollection<ResourceSearchParameterStatus> Statuses, DateTimeOffset? LastUpdated)> GetSearchParameterStatusUpdates(CancellationToken cancellationToken, DateTimeOffset? startLastUpdated = null)
        {
            var searchParamStatuses = await _searchParameterStatusDataStore.GetSearchParameterStatuses(cancellationToken, startLastUpdated);
            var lastUpdated = searchParamStatuses.Any() ? searchParamStatuses.Max(_ => _.LastUpdated) : (DateTimeOffset?)null;
            return (searchParamStatuses, lastUpdated);
        }

        // This is just a convenience wrapper method that should not be going to store directly
        public async Task<IReadOnlyCollection<ResourceSearchParameterStatus>> GetAllSearchParameterStatus(CancellationToken cancellationToken)
        {
            return (await GetSearchParameterStatusUpdates(cancellationToken)).Statuses;
        }

        // This is a convenience wrapper method
        public async Task<CacheConsistencyResult> CheckCacheConsistencyAsync(DateTime updateEventsSince, DateTime activeHostsSince, CancellationToken cancellationToken)
        {
            return await _searchParameterStatusDataStore.CheckCacheConsistencyAsync(updateEventsSince, activeHostsSince, cancellationToken);
        }
    }
}
