// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Runtime.Caching;
using EnsureThat;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.Core.Features.Storage
{
    public sealed class FhirMemoryCache<T> : IMemoryCache<T>
    {
        private const int DefaultLimitSizeInMegabytes = 50;
        private const int DefaultExpirationTimeInMinutes = 24 * 60;

        private readonly string _cacheName;
        private readonly ILogger _logger;
        private readonly ObjectCache _cache;
        private readonly TimeSpan _expirationTime;
        private readonly bool _ignoreCase;
        private readonly object _lock;

        public FhirMemoryCache(string name, ILogger logger, bool ignoreCase = false)
            : this(
                  name,
                  limitSizeInMegabytes: DefaultLimitSizeInMegabytes,
                  expirationTime: TimeSpan.FromMinutes(DefaultExpirationTimeInMinutes),
                  logger)
        {
        }

        public FhirMemoryCache(string name, int limitSizeInMegabytes, TimeSpan expirationTime, ILogger logger, bool ignoreCase = false)
        {
            EnsureArg.IsNotNull(name, nameof(name));
            EnsureArg.IsGt(limitSizeInMegabytes, 0, nameof(name));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _cacheName = name;

            _cache = new MemoryCache(
                _cacheName,
                new NameValueCollection()
                {
                    { "CacheMemoryLimitMegabytes", limitSizeInMegabytes.ToString() },
                });

            _expirationTime = expirationTime;

            _logger = logger;

            _ignoreCase = ignoreCase;

            _lock = new object();
        }

        public long CacheMemoryLimit => ((MemoryCache)_cache).CacheMemoryLimit;

        /// <summary>
        /// Get or add the value to cache.
        /// </summary>
        /// <typeparam name="T">Type of the value in cache</typeparam>
        /// <param name="key">Key</param>
        /// <param name="value">Value</param>
        /// <returns>Value in cache</returns>
        public T GetOrAdd(string key, T value)
        {
            key = FormatKey(key);

            lock (_lock)
            {
                if (_cache.Contains(key))
                {
                    return (T)_cache[key];
                }

                AddInternal(key, value);
            }

            return value;
        }

        /// <summary>
        /// Add the value to cache if it does not exist.
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="value">Value</param>
        /// <returns>Returns true if the item was added to the cache</returns>
        public bool TryAdd(string key, T value)
        {
            key = FormatKey(key);

            lock (_lock)
            {
                if (_cache.Contains(key))
                {
                    return false;
                }

                AddInternal(key, value);
            }

            return true;
        }

        /// <summary>
        /// Add a range of values to the cache.
        /// </summary>
        /// <param name="keyValuePairs">Range of values</param>
        public void AddRange(IReadOnlyDictionary<string, T> keyValuePairs)
        {
            foreach (KeyValuePair<string, T> item in keyValuePairs)
            {
                string key = FormatKey(item.Key);
                AddInternal(key, item.Value);
            }
        }

        /// <summary>
        /// Get an item from the cache.
        /// </summary>
        /// <param name="key">Key</param>
        /// <returns>Value</returns>
        public T Get(string key)
        {
            key = FormatKey(key);

            return (T)_cache[key];
        }

        /// <summary>
        /// Try to retrieve an item from cache, if it does not exist then returns the <see cref="default"/> for that generic type.
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="value">Value</param>
        /// <returns>True if the value exists in cache</returns>
        public bool TryGet(string key, out T value)
        {
            key = FormatKey(key);

            lock (_lock)
            {
                if (_cache.Contains(key))
                {
                    value = (T)_cache[key];
                    return true;
                }

                _logger.LogTrace("Item does not exist in '{CacheName}' cache. Returning default value.", _cacheName);
                value = default;
            }

            return false;
        }

        /// <summary>
        /// Removed the item indexed by the key.
        /// </summary>
        /// <param name="key">Key of the item to be removed from cache.</param>
        /// <returns>Returns false if the items does not exist in cache.</returns>
        public bool Remove(string key)
        {
            key = FormatKey(key);

            object objectInCache = _cache.Remove(key);

            return objectInCache != null;
        }

        private string FormatKey(string key) => _ignoreCase ? key.ToLowerInvariant() : key;

        private bool AddInternal(string key, T value)
        {
            CacheItemPolicy cachePolicy = new CacheItemPolicy()
            {
                Priority = CacheItemPriority.Default,
                SlidingExpiration = _expirationTime,
            };

            return _cache.Add(key, value, cachePolicy);
        }
    }
}
