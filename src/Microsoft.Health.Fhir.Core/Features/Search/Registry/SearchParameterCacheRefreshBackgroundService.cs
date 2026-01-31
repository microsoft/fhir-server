// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Security.Cryptography;
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
        private readonly SemaphoreSlim _refreshSemaphore;
        private bool _isInitialized;
        private CancellationToken _stoppingToken;
        private DateTime _lastForceRefreshTime;

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

            // Get refresh interval from configuration (default 20 seconds, minimum 1 second)
            var refreshIntervalSeconds = Math.Max(1, _coreFeatureConfiguration.Value.SearchParameterCacheRefreshIntervalSeconds);
            _refreshInterval = TimeSpan.FromSeconds(refreshIntervalSeconds);

            _logger.LogInformation("SearchParameter cache refresh background service initialized with {RefreshInterval} interval.", _refreshInterval);

            // Create timer but don't start it yet - wait for SearchParametersInitializedNotification
            _refreshTimer = new Timer(OnRefreshTimer, null, Timeout.InfiniteTimeSpan, _refreshInterval);

            // Create semaphore to prevent concurrent refresh operations (max 1 concurrent operation)
            _refreshSemaphore = new SemaphoreSlim(1, 1);

            // Initialize last force refresh time to now with random offset (0-5 minutes) to stagger force refreshes across instances
            // We use UtcNow instead of MinValue because SearchParameters are already loaded during initialization,
            // so there's no need to do a full refresh on first timer execution
            var randomOffsetMinutes = RandomNumberGenerator.GetInt32(0, 6); // 0-5 minutes
            _lastForceRefreshTime = DateTime.UtcNow.AddMinutes(randomOffsetMinutes);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _stoppingToken = stoppingToken;
            _logger.LogInformation("SearchParameterCacheRefreshBackgroundService starting...");

            try
            {
                // Wait for search parameters to be initialized before starting the refresh loop
                while (!_isInitialized && !stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }

                if (stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("SearchParameterCacheRefreshBackgroundService was cancelled before initialization completed.");
                    return;
                }

                _logger.LogInformation("Search parameters initialized. Starting cache refresh loop.");

                // Keep the service running until cancellation is requested
                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("SearchParameterCacheRefreshBackgroundService stopping due to cancellation request.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during SearchParameter cache refresh background service execution.");
                throw;
            }
            finally
            {
                // Stop the timer when ExecuteAsync completes to prevent it from continuing to fire
                // after the service has stopped and potentially after service provider disposal
                _refreshTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                _logger.LogInformation("SearchParameterCacheRefreshBackgroundService stopped.");
            }
        }

        private async void OnRefreshTimer(object state)
        {
            // Check if service is shutting down before attempting any operations
            if (_stoppingToken.IsCancellationRequested || !_isInitialized)
            {
                return;
            }

            // Try to acquire the semaphore immediately - if we can't, it means another refresh is already running
            if (!await _refreshSemaphore.WaitAsync(0, _stoppingToken))
            {
                _logger.LogDebug("SearchParameter cache refresh already in progress. Skipping this timer execution to prevent concurrent operations.");
                return;
            }

            try
            {
                var timeSinceLastForceRefresh = DateTime.UtcNow - _lastForceRefreshTime;
                var shouldForceRefresh = timeSinceLastForceRefresh >= TimeSpan.FromHours(1) || true;

                if (shouldForceRefresh)
                {
                    _logger.LogInformation("Performing full SearchParameter database refresh (last force refresh: {TimeSinceLastRefresh} ago).", timeSinceLastForceRefresh);

                    await _searchParameterOperations.GetAndApplySearchParameterUpdates(_stoppingToken, forceFullRefresh: true);

                    ////// Get ALL search parameters from database to ensure complete synchronization
                    ////var allSearchParameterStatus = await _searchParameterStatusManager.GetAllSearchParameterStatus(_stoppingToken);

                    ////// Check if shutdown was requested after the async call
                    ////if (_stoppingToken.IsCancellationRequested)
                    ////{
                    ////    _logger.LogDebug("SearchParameter cache refresh was cancelled during database fetch.");
                    ////    return;
                    ////}

                    ////_logger.LogInformation("Retrieved {Count} search parameters from database for full refresh.", allSearchParameterStatus.Count);

                    ////// Apply all search parameters (this will recalculate the hash)
                    ////await _searchParameterStatusManager.ApplySearchParameterStatus(allSearchParameterStatus, _stoppingToken);

                    _lastForceRefreshTime = DateTime.UtcNow;

                    // Check one more time if shutdown was requested after the async call
                    if (!_stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogInformation("SearchParameter full database refresh completed successfully.");
                    }
                }
                else
                {
                    _logger.LogDebug("Starting SearchParameter cache freshness check (last force refresh: {TimeSinceLastRefresh} ago).", timeSinceLastForceRefresh);

                    // Check if cache is stale using efficient database query
                    bool cacheIsStale = await _searchParameterStatusManager.EnsureCacheFreshnessAsync(_stoppingToken);

                    // Check again if shutdown was requested after the async call
                    if (_stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogDebug("SearchParameter cache refresh was cancelled during freshness check.");
                        return;
                    }

                    if (cacheIsStale)
                    {
                        _logger.LogInformation("SearchParameter cache is stale. Performing incremental SearchParameter synchronization.");

                        // Cache is stale - perform incremental SearchParameter lifecycle management
                        await _searchParameterOperations.GetAndApplySearchParameterUpdates(_stoppingToken);

                        // Check one more time if shutdown was requested after the async call
                        if (!_stoppingToken.IsCancellationRequested)
                        {
                            _logger.LogInformation("SearchParameter incremental refresh completed successfully.");
                        }
                    }
                    else
                    {
                        _logger.LogDebug("SearchParameter cache is up to date. No refresh needed.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("SearchParameter cache refresh was canceled during operation.");
            }
            catch (ObjectDisposedException)
            {
                _logger.LogDebug("SearchParameter cache refresh encountered disposed service during shutdown.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during SearchParameter cache refresh. Will retry on next scheduled interval.");

                // Don't rethrow from timer callback to avoid crashing the timer
            }
            finally
            {
                // Always release the semaphore to allow the next refresh operation
                _refreshSemaphore.Release();
            }
        }

        public async Task Handle(SearchParametersInitializedNotification notification, CancellationToken cancellationToken)
        {
            _logger.LogInformation("SearchParameters initialized. Starting cache refresh timer.");

            _isInitialized = true;

            // Only start the timer if the service hasn't been cancelled
            if (!_stoppingToken.IsCancellationRequested)
            {
                // Add random initial delay to stagger first refresh across instances
                // This prevents thundering herd problem when multiple pods start simultaneously
                var maxInitialDelaySeconds = Math.Max(0, _coreFeatureConfiguration.Value.SearchParameterCacheRefreshMaxInitialDelaySeconds);
                var randomInitialDelaySeconds = maxInitialDelaySeconds > 0
                    ? RandomNumberGenerator.GetInt32(0, maxInitialDelaySeconds + 1)
                    : 0;
                var initialDelay = TimeSpan.FromSeconds(randomInitialDelaySeconds);

                if (randomInitialDelaySeconds > 0)
                {
                    _logger.LogInformation("Starting cache refresh timer with {InitialDelay} initial delay to stagger instance startup.", initialDelay);
                }
                else
                {
                    _logger.LogInformation("Starting cache refresh timer immediately (no initial delay configured).");
                }

                // Start the timer with random initial delay, then use regular refresh interval
                _refreshTimer.Change(initialDelay, _refreshInterval);
            }

            await Task.CompletedTask;
        }

        public override void Dispose()
        {
            _refreshTimer?.Dispose();
            _refreshSemaphore?.Dispose();
            base.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
