// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Search.Caching;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;

namespace Microsoft.Health.Fhir.Core.Features.Search.BackgroundServices
{
    /// <summary>
    /// Background service that periodically synchronizes search parameter cache with the database
    /// to ensure Redis cache stays consistent and handles cache expiry scenarios.
    /// </summary>
    public class SearchParameterCacheSyncService : BackgroundService
    {
        private readonly ISearchParameterStatusDataStore _dataStore;
        private readonly ISearchParameterCache _distributedCache;
        private readonly RedisConfiguration _redisConfiguration;
        private readonly ILogger<SearchParameterCacheSyncService> _logger;
        private readonly TimeSpan _syncInterval;

        public SearchParameterCacheSyncService(
            ISearchParameterStatusDataStore dataStore,
            ISearchParameterCache distributedCache,
            IOptions<FhirServerCachingConfiguration> cachingConfiguration,
            ILogger<SearchParameterCacheSyncService> logger)
        {
            EnsureArg.IsNotNull(dataStore, nameof(dataStore));

            // Note: distributedCache can be null when Redis is disabled
            EnsureArg.IsNotNull(cachingConfiguration, nameof(cachingConfiguration));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _dataStore = dataStore;
            _distributedCache = distributedCache;
            _redisConfiguration = cachingConfiguration.Value.Redis;
            _logger = logger;

            // Sync at half the cache expiry time to ensure we refresh before expiration
            // But not less than 10 minutes and not more than 4 hours
            var searchParamConfig = _redisConfiguration.CacheTypes.TryGetValue("SearchParameters", out var config)
                ? config
                : new Microsoft.Health.Fhir.Core.Features.Caching.CacheTypeConfiguration
                {
                    CacheExpiry = TimeSpan.FromHours(1),
                };

            var halfCacheExpiry = TimeSpan.FromTicks(searchParamConfig.CacheExpiry.Ticks / 2);
            _syncInterval = halfCacheExpiry < TimeSpan.FromMinutes(10)
                ? TimeSpan.FromMinutes(10)
                : halfCacheExpiry > TimeSpan.FromHours(4)
                    ? TimeSpan.FromHours(4)
                    : halfCacheExpiry;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Only run if Redis is enabled
            if (_distributedCache == null || !_redisConfiguration.Enabled)
            {
                _logger.LogInformation("Redis cache is disabled, SearchParameterCacheSyncService will not run");
                return;
            }

            _logger.LogInformation("SearchParameterCacheSyncService started with sync interval: {SyncInterval}", _syncInterval);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(_syncInterval, stoppingToken);

                    if (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }

                    await PerformFullSyncAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("SearchParameterCacheSyncService was cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SearchParameterCacheSyncService encountered an unexpected error and will stop");
            }

            _logger.LogInformation("SearchParameterCacheSyncService stopped");
        }

        private async Task PerformFullSyncAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogDebug("Starting periodic full sync of search parameter cache");

                // Get all current search parameter statuses from database (source of truth)
                var databaseStatuses = await _dataStore.GetSearchParameterStatuses(cancellationToken);

                if (databaseStatuses.Count > 0)
                {
                    // Perform full replacement of cache with database data
                    await _distributedCache.SetAsync(databaseStatuses, cancellationToken);

                    _logger.LogInformation("Completed periodic full sync of search parameter cache, synced {Count} statuses", databaseStatuses.Count);
                }
                else
                {
                    _logger.LogWarning("No search parameter statuses found in database during periodic sync");
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Periodic cache sync was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during periodic search parameter cache sync - sync will be retried on next interval");
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("SearchParameterCacheSyncService is stopping");
            await base.StopAsync(cancellationToken);
        }
    }
}
