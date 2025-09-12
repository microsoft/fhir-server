// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Messages.Search;

namespace Microsoft.Health.Fhir.Core.Features.Search.Registry
{
    /// <summary>
    /// Background service that periodically ensures SearchParameter cache freshness by calling EnsureCacheFreshnessAsync.
    /// This service runs continuously to keep SearchParameters up-to-date across all instances.
    /// </summary>
    public class SearchParameterCacheRefreshBackgroundService : BackgroundService, INotificationHandler<SearchParametersInitializedNotification>
    {
        private readonly ISearchParameterStatusManager _searchParameterStatusManager;
        private readonly ISearchParameterOperations _searchParameterOperations;
        private readonly IOptions<CoreFeatureConfiguration> _coreFeatureConfiguration;
        private readonly ILogger<SearchParameterCacheRefreshBackgroundService> _logger;
        private readonly TimeSpan _refreshInterval;
        private readonly Timer _refreshTimer;
        private bool _isInitialized;

        public SearchParameterCacheRefreshBackgroundService(
            ISearchParameterStatusManager searchParameterStatusManager,
            ISearchParameterOperations searchParameterOperations,
            IOptions<CoreFeatureConfiguration> coreFeatureConfiguration,
            ILogger<SearchParameterCacheRefreshBackgroundService> logger)
        {
            _searchParameterStatusManager = EnsureArg.IsNotNull(searchParameterStatusManager, nameof(searchParameterStatusManager));
            _searchParameterOperations = EnsureArg.IsNotNull(searchParameterOperations, nameof(searchParameterOperations));
            _coreFeatureConfiguration = EnsureArg.IsNotNull(coreFeatureConfiguration, nameof(coreFeatureConfiguration));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));

            // Get refresh interval from configuration (default 1 minute, minimum 1 minute)
            var refreshIntervalMinutes = Math.Max(1, _coreFeatureConfiguration.Value.SearchParameterCacheRefreshIntervalMinutes);
            _refreshInterval = TimeSpan.FromMinutes(refreshIntervalMinutes);

            _logger.LogInformation("SearchParameter cache refresh background service initialized with {RefreshInterval} interval.", _refreshInterval);

            // Create timer but don't start it yet - wait for SearchParametersInitializedNotification
            _refreshTimer = new Timer(OnRefreshTimer, null, Timeout.InfiniteTimeSpan, _refreshInterval);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("SearchParameterCacheRefreshBackgroundService starting...");

            // Wait for search parameters to be initialized before starting the refresh loop
            while (!_isInitialized && !stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }

            _logger.LogInformation("Search parameters initialized. Starting cache refresh loop.");

            // Keep the service running until cancellation is requested
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "SearchParameter cache refresh was canceled. Will retry on next scheduled interval.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during SearchParameter cache refresh. Will retry on next scheduled interval.");
                throw;
            }
        }

        private async void OnRefreshTimer(object state)
        {
            if (!_isInitialized)
            {
                return; // Don't start refresh cycle until SearchParameters are initialized
            }

            try
            {
                _logger.LogDebug("Starting SearchParameter cache freshness check.");

                // First check if cache is stale using efficient database query
                bool cacheIsStale = await _searchParameterStatusManager.EnsureCacheFreshnessAsync(CancellationToken.None);

                if (cacheIsStale)
                {
                    _logger.LogInformation("SearchParameter cache is stale. Performing full SearchParameter synchronization.");

                    // Cache is stale - perform full SearchParameter lifecycle management
                    await _searchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);

                    _logger.LogInformation("SearchParameter cache refresh completed successfully.");
                }
                else
                {
                    _logger.LogDebug("SearchParameter cache is up to date. No refresh needed.");
                }
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "SearchParameter cache refresh was canceled. Will retry on next scheduled interval.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during SearchParameter cache refresh. Will retry on next scheduled interval.");
                throw;
            }
        }

        public async Task Handle(SearchParametersInitializedNotification notification, CancellationToken cancellationToken)
        {
            _logger.LogInformation("SearchParameters initialized. Starting cache refresh timer.");

            _isInitialized = true;

            // Start the timer now that search parameters are initialized
            _refreshTimer.Change(TimeSpan.Zero, _refreshInterval);

            await Task.CompletedTask;
        }

        public override void Dispose()
        {
            _refreshTimer?.Dispose();
            base.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
