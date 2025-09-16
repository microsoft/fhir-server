// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Caching;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;

namespace Microsoft.Health.Fhir.Core.Features.Caching.Redis
{
    /// <summary>
    /// Generic Redis-based implementation of distributed caching for any serializable type
    /// </summary>
    /// <typeparam name="T">The type of objects to cache</typeparam>
    public class RedisDistributedCache<T> : IDistributedCache<T>
        where T : class, ICacheItem
    {
        private readonly IDistributedCache _distributedCache;
        private readonly CacheTypeConfiguration _configuration;
        private readonly ILogger<RedisDistributedCache<T>> _logger;
        private readonly ISearchParameterStatusDataStore _dataStore;

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
        };

        // Cache keys
        private readonly string _allItemsKey;
        private readonly string _versionKey;
        private readonly string _individualKeyPrefix;
        private readonly string _healthCheckKey;
        private readonly string _lockKey;

        public RedisDistributedCache(
            IDistributedCache distributedCache,
            CacheTypeConfiguration configuration,
            ILogger<RedisDistributedCache<T>> logger,
            ISearchParameterStatusDataStore dataStore)
        {
            EnsureArg.IsNotNull(distributedCache, nameof(distributedCache));
            EnsureArg.IsNotNull(configuration, nameof(configuration));
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(dataStore, nameof(dataStore));

            _distributedCache = distributedCache;
            _configuration = configuration;
            _logger = logger;
            _dataStore = dataStore;

            _allItemsKey = $"{_configuration.KeyPrefix}:all";
            _versionKey = $"{_configuration.KeyPrefix}:version";
            _individualKeyPrefix = $"{_configuration.KeyPrefix}:item:";
            _healthCheckKey = $"{_configuration.KeyPrefix}:healthcheck";
            _lockKey = $"{_configuration.KeyPrefix}:lock";
        }

        public async Task<IReadOnlyCollection<T>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("Retrieving all {Type} items from Redis cache", typeof(T).Name);

                var cachedData = await _distributedCache.GetAsync(_allItemsKey, cancellationToken);
                if (cachedData == null)
                {
                    _logger.LogDebug("No cached {Type} items found in Redis", typeof(T).Name);
                    return Array.Empty<T>();
                }

                var decompressedData = _configuration.EnableCompression
                    ? DecompressData(cachedData)
                    : cachedData;

                var json = Encoding.UTF8.GetString(decompressedData);
                var items = JsonSerializer.Deserialize<List<T>>(json, SerializerOptions);

                _logger.LogDebug("Retrieved {Count} {Type} items from Redis cache", items?.Count ?? 0, typeof(T).Name);
                return items?.Cast<T>().ToList() ?? new List<T>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving {Type} items from Redis cache", typeof(T).Name);
                throw;
            }
        }

        public async Task<IReadOnlyCollection<T>> GetUpdatedAsync(DateTimeOffset lastUpdated, CancellationToken cancellationToken = default)
        {
            try
            {
                var allItems = await GetAllAsync(cancellationToken);
                var updatedItems = allItems.Where(item => item.LastUpdated > lastUpdated).ToList();

                _logger.LogDebug(
                    "Found {Count} {Type} items updated after {LastUpdated}",
                    updatedItems.Count,
                    typeof(T).Name,
                    lastUpdated);

                return updatedItems;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving updated {Type} items from Redis cache", typeof(T).Name);
                throw;
            }
        }

        public async Task<T> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNullOrWhiteSpace(key, nameof(key));

            try
            {
                var cacheKey = $"{_individualKeyPrefix}{Convert.ToBase64String(Encoding.UTF8.GetBytes(key))}";
                var cachedData = await _distributedCache.GetAsync(cacheKey, cancellationToken);

                if (cachedData == null)
                {
                    _logger.LogDebug("{Type} item not found in cache: {Key}", typeof(T).Name, key);
                    return null;
                }

                var decompressedData = _configuration.EnableCompression
                    ? DecompressData(cachedData)
                    : cachedData;

                var json = Encoding.UTF8.GetString(decompressedData);
                var item = JsonSerializer.Deserialize<T>(json, SerializerOptions);

                _logger.LogDebug("Retrieved {Type} item from cache: {Key}", typeof(T).Name, key);
                return item;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving {Type} item from Redis cache: {Key}", typeof(T).Name, key);
                throw;
            }
        }

        public async Task SetAsync(IReadOnlyCollection<T> items, CancellationToken cancellationToken = default)
        {
            await UpsertAsync(items, replaceAll: true, cancellationToken);
        }

        public async Task UpsertAsync(IReadOnlyCollection<T> items, bool replaceAll = false, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(items, nameof(items));

            if (!items.Any())
            {
                return;
            }

            // Use distributed lock for multi-instance coordination
            await ExecuteWithDistributedLockAsync(
                async () =>
                {
                    _logger.LogDebug(
                        "Upserting {Count} {Type} items in Redis (replaceAll: {ReplaceAll})",
                        items.Count,
                        typeof(T).Name,
                        replaceAll);

                    IReadOnlyCollection<T> itemsToCache;

                    if (replaceAll)
                    {
                        itemsToCache = items;
                    }
                    else
                    {
                        // Merge with existing cache - now protected by distributed lock
                        var existingItems = await GetAllAsync(cancellationToken);
                        var existingDict = existingItems.ToDictionary(item => item.CacheKey, item => item);

                        _logger.LogDebug("Merging {NewCount} new items with {ExistingCount} existing items", items.Count, existingItems.Count);

                        // Track which items are being updated vs added
                        var itemsUpdated = 0;
                        var itemsAdded = 0;

                        // Update existing or add new items
                        foreach (var item in items)
                        {
                            if (existingDict.ContainsKey(item.CacheKey))
                            {
                                itemsUpdated++;
                                _logger.LogDebug("Updating existing cache item: {CacheKey} (LastUpdated: {LastUpdated})", item.CacheKey, item.LastUpdated);
                            }
                            else
                            {
                                itemsAdded++;
                                _logger.LogDebug("Adding new cache item: {CacheKey} (LastUpdated: {LastUpdated})", item.CacheKey, item.LastUpdated);
                            }

                            existingDict[item.CacheKey] = item;
                        }

                        itemsToCache = existingDict.Values.ToList();

                        _logger.LogInformation(
                            "Cache merge completed - Items added: {Added}, Items updated: {Updated}, Total items to cache: {TotalCount}",
                            itemsAdded,
                            itemsUpdated,
                            itemsToCache.Count);

                        // Log cache key conflicts if any
                        var conflictingItems = items.GroupBy(item => item.CacheKey).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
                        if (conflictingItems.Any())
                        {
                            _logger.LogWarning(
                                "Cache key conflicts detected for items: {ConflictKeys}. Consider using unique keys to avoid unexpected behavior.",
                                string.Join(", ", conflictingItems));
                        }
                    }

                    // Duplicate check
                    var duplicateGroups = items.GroupBy(item => item.CacheKey).Where(g => g.Count() > 1).ToList();
                    if (duplicateGroups.Any())
                    {
                        _logger.LogWarning(
                            "Found {DuplicateKeyCount} duplicate cache keys in {Type} items, {TotalDuplicates} items will be dropped: {Keys}",
                            duplicateGroups.Count,
                            typeof(T).Name,
                            duplicateGroups.Sum(g => g.Count() - 1),
                            string.Join(", ", duplicateGroups.Select(g => $"{g.Key} ({g.Count()}x)")));
                    }

                    _logger.LogInformation(
                        "Before caching: Input items: {InputCount}, Items to cache: {CacheCount}",
                        items.Count,
                        itemsToCache.Count);

                    var json = JsonSerializer.Serialize(itemsToCache, SerializerOptions);
                    var data = Encoding.UTF8.GetBytes(json);

                    var compressedData = _configuration.EnableCompression
                        ? CompressData(data)
                        : data;

                    var options = new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = _configuration.CacheExpiry,
                    };

                    // Cache all items together
                    await _distributedCache.SetAsync(_allItemsKey, compressedData, options, cancellationToken);

                    // Also cache individual items for faster lookups
                    var individualCacheTasks = itemsToCache.Select(async item =>
                    {
                        var individualJson = JsonSerializer.Serialize(item, SerializerOptions);
                        var individualData = Encoding.UTF8.GetBytes(individualJson);
                        var individualCompressedData = _configuration.EnableCompression
                            ? CompressData(individualData)
                            : individualData;

                        var cacheKey = $"{_individualKeyPrefix}{Convert.ToBase64String(Encoding.UTF8.GetBytes(item.CacheKey))}";
                        await _distributedCache.SetAsync(cacheKey, individualCompressedData, options, cancellationToken);
                    });

                    await Task.WhenAll(individualCacheTasks);

                    // Update cache version if enabled
                    if (_configuration.EnableVersioning)
                    {
                        await SetCacheVersionAsync(DateTimeOffset.UtcNow, cancellationToken);
                    }

                    // Verification after caching
                    var verifyItems = await GetAllAsync(cancellationToken);
                    if (verifyItems.Count != itemsToCache.Count)
                    {
                        _logger.LogError(
                            "Cache verification FAILED! Expected: {Expected}, Actual: {Actual}, Difference: {Missing}",
                            itemsToCache.Count,
                            verifyItems.Count,
                            itemsToCache.Count - verifyItems.Count);
                    }
                    else
                    {
                        _logger.LogDebug("Cache verification successful: {Count} {Type} items confirmed in cache", verifyItems.Count, typeof(T).Name);
                    }

                    _logger.LogInformation(
                        "Successfully cached {Count} {Type} items in Redis (total cached: {TotalCached})",
                        items.Count,
                        typeof(T).Name,
                        itemsToCache.Count);
                },
                cancellationToken);
        }

        public async Task RemoveAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(keys, nameof(keys));

            if (!keys.Any())
            {
                return;
            }

            try
            {
                _logger.LogDebug("Removing {Count} {Type} items from Redis cache", keys.Count, typeof(T).Name);

                // Remove individual cache entries
                var removeTasks = keys.Select(key =>
                {
                    var cacheKey = $"{_individualKeyPrefix}{Convert.ToBase64String(Encoding.UTF8.GetBytes(key))}";
                    return _distributedCache.RemoveAsync(cacheKey, cancellationToken);
                });

                await Task.WhenAll(removeTasks);

                // Invalidate the "all" cache since it's now stale
                await _distributedCache.RemoveAsync(_allItemsKey, cancellationToken);

                // Update cache version if enabled
                if (_configuration.EnableVersioning)
                {
                    await SetCacheVersionAsync(DateTimeOffset.UtcNow, cancellationToken);
                }

                _logger.LogInformation("Successfully removed {Count} {Type} items from Redis cache", keys.Count, typeof(T).Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing {Type} items from Redis cache", typeof(T).Name);
                throw;
            }
        }

        public async Task InvalidateAllAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Invalidating all {Type} cache entries in Redis", typeof(T).Name);

                await _distributedCache.RemoveAsync(_allItemsKey, cancellationToken);

                if (_configuration.EnableVersioning)
                {
                    await _distributedCache.RemoveAsync(_versionKey, cancellationToken);
                }

                _logger.LogInformation("Successfully invalidated all {Type} cache entries in Redis", typeof(T).Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error invalidating {Type} cache in Redis", typeof(T).Name);
                throw;
            }
        }

        public async Task<DateTimeOffset?> GetCacheVersionAsync(CancellationToken cancellationToken = default)
        {
            if (!_configuration.EnableVersioning)
            {
                return null;
            }

            try
            {
                var versionData = await _distributedCache.GetStringAsync(_versionKey, cancellationToken);

                if (string.IsNullOrEmpty(versionData))
                {
                    return null;
                }

                return DateTimeOffset.Parse(versionData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving {Type} cache version from Redis", typeof(T).Name);
                return null;
            }
        }

        public async Task SetCacheVersionAsync(DateTimeOffset version, CancellationToken cancellationToken = default)
        {
            if (!_configuration.EnableVersioning)
            {
                return;
            }

            try
            {
                var options = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = _configuration.CacheExpiry,
                };

                await _distributedCache.SetStringAsync(_versionKey, version.ToString("O"), options, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting {Type} cache version in Redis", typeof(T).Name);
                throw;
            }
        }

        public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var testValue = DateTime.UtcNow.ToString("O");

                await _distributedCache.SetStringAsync(_healthCheckKey, testValue, cancellationToken);
                var retrievedValue = await _distributedCache.GetStringAsync(_healthCheckKey, cancellationToken);

                var isHealthy = testValue == retrievedValue;
                _logger.LogDebug("{Type} Redis health check result: {IsHealthy}", typeof(T).Name, isHealthy);

                return isHealthy;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{Type} Redis health check failed", typeof(T).Name);
                return false;
            }
        }

        public async Task<bool> IsCacheStaleAsync(CancellationToken cancellationToken = default)
        {
            if (!_configuration.EnableVersioning)
            {
                return false; // If versioning is disabled, assume cache is fresh
            }

            try
            {
                var cacheVersion = await GetCacheVersionAsync(cancellationToken);

                if (!cacheVersion.HasValue)
                {
                    _logger.LogDebug("{Type} cache version not found - cache appears to be stale", typeof(T).Name);
                    return true;
                }

                var timeSinceLastUpdate = DateTimeOffset.UtcNow - cacheVersion.Value;
                var isStaleByTime = timeSinceLastUpdate > _configuration.CacheExpiry;

                // Check count mismatch if count verification is enabled
                bool isStaleByCount = false;

                try
                {
                    var cacheItems = await GetAllAsync(cancellationToken);
                    var cacheCount = cacheItems.Count;
                    var databaseCount = await _dataStore.GetTotalCount(cancellationToken);

                    isStaleByCount = cacheCount != databaseCount;

                    _logger.LogDebug(
                        "{Type} count verification: cache count {CacheCount}, database count {DatabaseCount}, count mismatch {IsStaleByCount}",
                        typeof(T).Name,
                        cacheCount,
                        databaseCount,
                        isStaleByCount);

                    if (isStaleByCount)
                    {
                        _logger.LogWarning(
                            "{Type} cache count mismatch detected - cache has {CacheCount} items but database has {DatabaseCount} items",
                            typeof(T).Name,
                            cacheCount,
                            databaseCount);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error checking {Type} cache count, assuming count mismatch", typeof(T).Name);
                    isStaleByCount = true;
                }

                var isStale = isStaleByTime || isStaleByCount;

                _logger.LogDebug(
                    "{Type} cache staleness check: last update {LastUpdate}, time since update {TimeSince}, expiry {Expiry}, is stale by time {IsStaleByTime}, is stale by count {IsStaleByCount}, overall is stale {IsStale}",
                    typeof(T).Name,
                    cacheVersion.Value,
                    timeSinceLastUpdate,
                    _configuration.CacheExpiry,
                    isStaleByTime,
                    isStaleByCount,
                    isStale);

                return isStale;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error checking {Type} cache staleness, assuming stale", typeof(T).Name);
                return true;
            }
        }

        /// <summary>
        /// Executes the given action with a distributed lock using Redis.
        /// This prevents concurrent modifications across multiple application instances.
        /// </summary>
        private async Task ExecuteWithDistributedLockAsync(Func<Task> action, CancellationToken cancellationToken)
        {
            const int lockTimeoutSeconds = 30;
            const int maxRetryAttempts = 10;
            const int baseDelayMs = 100;

            var lockValue = Environment.MachineName + "_" + Environment.ProcessId + "_" + Guid.NewGuid().ToString("N")[..8];
            var lockExpiry = TimeSpan.FromSeconds(lockTimeoutSeconds);

            for (int attempt = 0; attempt < maxRetryAttempts; attempt++)
            {
                try
                {
                    // Try to acquire distributed lock using Redis SET with NX (not exists) and EX (expiry)
                    var lockAcquired = await TryAcquireDistributedLockAsync(lockValue, lockExpiry, cancellationToken);

                    if (lockAcquired)
                    {
                        try
                        {
                            _logger.LogDebug("Distributed lock acquired for {Type} cache operations", typeof(T).Name);

                            // Execute the protected operation
                            await action();
                            return; // Success!
                        }
                        finally
                        {
                            // Release the lock
                            await ReleaseDistributedLockAsync(lockValue, cancellationToken);
                            _logger.LogDebug("Distributed lock released for {Type} cache operations", typeof(T).Name);
                        }
                    }

                    // Lock not acquired, wait before retry
                    var delay = TimeSpan.FromMilliseconds(baseDelayMs * Math.Pow(2, attempt));
                    await Task.Delay(delay, cancellationToken);

                    _logger.LogDebug(
                        "Failed to acquire distributed lock for {Type}, attempt {Attempt}/{MaxAttempts}",
                        typeof(T).Name,
                        attempt + 1,
                        maxRetryAttempts);
                }
                catch (OperationCanceledException)
                {
                    throw; // Don't retry on cancellation
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error during distributed lock attempt {Attempt} for {Type}", attempt + 1, typeof(T).Name);

                    if (attempt == maxRetryAttempts - 1)
                    {
                        throw; // Re-throw on final attempt
                    }
                }
            }

            throw new InvalidOperationException($"Failed to acquire distributed lock for {typeof(T).Name} cache after {maxRetryAttempts} attempts");
        }

        /// <summary>
        /// Attempts to acquire a distributed lock using Redis SET NX EX command
        /// </summary>
        private async Task<bool> TryAcquireDistributedLockAsync(string lockValue, TimeSpan expiry, CancellationToken cancellationToken)
        {
            try
            {
                // Check if lock already exists
                var existingLock = await _distributedCache.GetStringAsync(_lockKey, cancellationToken);

                if (existingLock == null)
                {
                    // Try to set the lock key with our unique value
                    var options = new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = expiry,
                    };

                    await _distributedCache.SetStringAsync(_lockKey, lockValue, options, cancellationToken);

                    // Verify we actually got the lock (handle race conditions)
                    var verifyLock = await _distributedCache.GetStringAsync(_lockKey, cancellationToken);
                    return verifyLock == lockValue;
                }

                return false; // Lock already exists
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error acquiring distributed lock for {Type}", typeof(T).Name);
                return false;
            }
        }

        /// <summary>
        /// Releases the distributed lock if we own it
        /// </summary>
        private async Task ReleaseDistributedLockAsync(string lockValue, CancellationToken cancellationToken)
        {
            try
            {
                // Only release if we own the lock (prevents releasing someone else's lock)
                var existingLock = await _distributedCache.GetStringAsync(_lockKey, cancellationToken);

                if (existingLock == lockValue)
                {
                    await _distributedCache.RemoveAsync(_lockKey, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error releasing distributed lock for {Type}", typeof(T).Name);

                // Don't throw - we don't want to mask the original operation's exception
            }
        }

        private static byte[] CompressData(byte[] data)
        {
            using var compressedStream = new System.IO.MemoryStream();
            using var compressionStream = new GZipStream(compressedStream, CompressionLevel.Fastest);
            compressionStream.Write(data, 0, data.Length);
            compressionStream.Close();
            return compressedStream.ToArray();
        }

        private static byte[] DecompressData(byte[] compressedData)
        {
            using var compressedStream = new System.IO.MemoryStream(compressedData);
            using var decompressionStream = new GZipStream(compressedStream, CompressionMode.Decompress);
            using var resultStream = new System.IO.MemoryStream();
            decompressionStream.CopyTo(resultStream);
            return resultStream.ToArray();
        }
    }
}
