// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text;
using EnsureThat;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Health.Fhir.Core.Features.Storage
{
    public sealed class FhirMemoryCache<T> : IFhirMemoryCache<T>, IDisposable
    {
        private const int DefaultLimit = 50 * 1024 * 1024;
        private const int DefaultExpirationTimeInMinutes = 24 * 60;
        private const double DefaultCompactionPercentage = 0.05;

        private readonly string _cacheName;
        private readonly ILogger _logger;
        private readonly MemoryCache _cache;
        private readonly MemoryCacheOptions _cacheOptions;
        private readonly TimeSpan _entryExpirationTime;
        private readonly bool _ignoreCase;
        private readonly FhirCacheLimitType _limitType;

        private bool _disposed;

        public FhirMemoryCache(string name, ILogger logger, bool ignoreCase = false, FhirCacheLimitType limitType = FhirCacheLimitType.Byte)
            : this(
                name,
                sizeLimit: DefaultLimit,
                entryExpirationTime: TimeSpan.FromMinutes(DefaultExpirationTimeInMinutes),
                logger,
                ignoreCase,
                limitType: limitType)
        {
        }

        public FhirMemoryCache(
            string name,
            long sizeLimit,
            TimeSpan entryExpirationTime,
            ILogger logger,
            bool ignoreCase = false,
            double compactionPercentage = DefaultCompactionPercentage,
            FhirCacheLimitType limitType = FhirCacheLimitType.Byte)
        {
            EnsureArg.IsNotNull(name, nameof(name));
            EnsureArg.IsGt(sizeLimit, 0, nameof(sizeLimit));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _cacheName = name;

            _cacheOptions = new MemoryCacheOptions()
            {
                SizeLimit = sizeLimit, // Sets the maximum size of the cache.
                CompactionPercentage = compactionPercentage, // Sets the amount the cache is compacted by when the maximum size is exceeded.
            };

            _cache = new MemoryCache(Options.Create(_cacheOptions));

            _entryExpirationTime = entryExpirationTime;
            _logger = logger;
            _ignoreCase = ignoreCase;
            _limitType = limitType;

            _disposed = false;
        }

        public string Name => _cacheName;

        public double CompactionPercentage => _cacheOptions.CompactionPercentage;

        public long CacheMemoryLimit => _cacheOptions.SizeLimit ?? 0;

        public long Count => _cache.Count;

        /// <summary>
        /// Get or add the value to cache.
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="value">Value</param>
        /// <returns>Value in cache</returns>
        public T GetOrAdd(string key, T value)
        {
            return GetOrAdd(key, value, FhirMemoryCacheItemPriority.Normal);
        }

        /// <summary>
        /// Get or add the value to cache.
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="value">Value</param>
        /// <param name="priority">Priority</param>
        /// <returns>Value in cache</returns>
        public T GetOrAdd(string key, T value, FhirMemoryCacheItemPriority priority)
        {
            CheckDisposed();

            EnsureArg.IsNotNullOrWhiteSpace(key, nameof(key));
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            key = FormatKey(key);

            T cachedValue = _cache.GetOrCreate(key, entry =>
            {
                MemoryCacheEntryOptions newCacheEntryPolicy = GetDefaultCacheItemPolicy(SizeOfEntryInCache(key, value), priority);
                SetEntryPolicy(entry, newCacheEntryPolicy);

                return value;
            });

            return cachedValue;
        }

        /// <summary>
        /// Add the value to cache if it does not exist.
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="value">Value</param>
        /// <returns>Returns true if the item was added to the cache, returns false if there is an item with the same key in cache.</returns>
        public bool TryAdd(string key, T value)
        {
            return TryAdd(key, value, FhirMemoryCacheItemPriority.Normal);
        }

        /// <summary>
        /// Add the value to cache if it does not exist.
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="value">Value</param>
        /// <param name="priority">Priority</param>
        /// <returns>Returns true if the item was added to the cache, returns false if there is an item with the same key in cache.</returns>
        public bool TryAdd(string key, T value, FhirMemoryCacheItemPriority priority)
        {
            CheckDisposed();

            EnsureArg.IsNotNullOrWhiteSpace(key, nameof(key));
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            key = FormatKey(key);

            MemoryCacheEntryOptions newCacheEntryPolicy = GetDefaultCacheItemPolicy(SizeOfEntryInCache(key, value), priority);

            _cache.Set<T>(key, value, newCacheEntryPolicy);

            // After inserting, check if the value is persisted.
            object cachedValue = _cache.Get(key);

            return cachedValue != null;
        }

        /// <summary>
        /// Get an item from the cache.
        /// </summary>
        /// <param name="key">Key</param>
        /// <returns>Value</returns>
        public T Get(string key)
        {
            CheckDisposed();

            key = FormatKey(key);
            object cachedValue = _cache.Get(key);

            return cachedValue == null ? default(T) : (T)cachedValue;
        }

        /// <summary>
        /// Try to retrieve an item from cache, if it does not exist then returns the default for that generic type.
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="value">Value</param>
        /// <returns>True if the value exists in cache</returns>
        public bool TryGet(string key, out T value)
        {
            CheckDisposed();

            key = FormatKey(key);

            if (_cache.TryGetValue(key, out object cachedItem))
            {
                value = (T)cachedItem;
                return true;
            }

            _logger.LogTrace("Item does not exist in '{CacheName}' cache. Returning default value.", _cacheName);
            value = default;

            return false;
        }

        /// <summary>
        /// Removed the item indexed by the key.
        /// </summary>
        /// <param name="key">Key of the item to be removed from cache.</param>
        public bool Remove(string key)
        {
            CheckDisposed();

            key = FormatKey(key);

            if (_cache.TryGetValue(key, out object cachedItem))
            {
                _cache.Remove(key);
                return true;
            }

            return false;
        }

        public void Clear()
        {
            CheckDisposed();
            _cache.Compact(1.0);
        }

        public void Dispose()
        {
            Dispose(disposing: true);
        }

        private long SizeOfEntryInCache(string key, T value)
        {
            if (_limitType == FhirCacheLimitType.Count)
            {
                return 1;
            }
            else if (_limitType == FhirCacheLimitType.Byte)
            {
                Type typeOfT = typeof(T);

                int keySizeInBytes = ASCIIEncoding.Unicode.GetByteCount(key);

                int valueSizeInBytes = 0;
                if (typeOfT == typeof(int))
                {
                    valueSizeInBytes = sizeof(int);
                }
                else if (typeOfT == typeof(long))
                {
                    valueSizeInBytes = sizeof(long);
                }
                else if (typeOfT == typeof(byte[]))
                {
                    valueSizeInBytes = (value as byte[]).Length;
                }
                else if (typeOfT == typeof(string))
                {
                    valueSizeInBytes = ASCIIEncoding.Unicode.GetByteCount(value.ToString());
                }
                else
                {
                    throw new InvalidOperationException("Unknown type to compute the size.");
                }

                return keySizeInBytes + valueSizeInBytes;
            }
            else
            {
                throw new InvalidOperationException("Unknown limit type for a memory cache.");
            }
        }

        private static void SetEntryPolicy(ICacheEntry entry, MemoryCacheEntryOptions options)
        {
            entry.AbsoluteExpirationRelativeToNow = options.AbsoluteExpirationRelativeToNow;
            entry.Size = options.Size; // Defines the size of the new entry being added to cache.
            entry.Priority = options.Priority;
        }

        private static CacheItemPriority ParsePriority(FhirMemoryCacheItemPriority itemPriority)
        {
            // Using a switch/case for safer parse logic, instead of int mapping.
            switch (itemPriority)
            {
                case FhirMemoryCacheItemPriority.Low:
                    return CacheItemPriority.Low;
                case FhirMemoryCacheItemPriority.Normal:
                    return CacheItemPriority.Normal;
                case FhirMemoryCacheItemPriority.High:
                    return CacheItemPriority.High;
                case FhirMemoryCacheItemPriority.NeverRemove:
                    return CacheItemPriority.NeverRemove;
                default:
                    throw new InvalidOperationException("Unknown priority for a memory cache item.");
            }
        }

        private MemoryCacheEntryOptions GetDefaultCacheItemPolicy(long size, FhirMemoryCacheItemPriority priority) => new MemoryCacheEntryOptions()
        {
            AbsoluteExpirationRelativeToNow = _entryExpirationTime,
            Size = size, // Defines the size of the new entry being added to cache.
            Priority = ParsePriority(priority),
        };

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _cache.Dispose();
                }

                _disposed = true;
            }
        }

        private void CheckDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        private string FormatKey(string key) => _ignoreCase ? key.ToLowerInvariant() : key;
    }
}
