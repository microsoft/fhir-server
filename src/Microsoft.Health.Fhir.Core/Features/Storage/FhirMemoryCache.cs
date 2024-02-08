// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Runtime.Caching;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Storage
{
    public sealed class FhirMemoryCache<T>
    {
        private const int DefaultLimitSizeInMegabytes = 50;
        private const int DefaultExpirationTimeInMinutes = 60;

        private readonly ObjectCache _cache = MemoryCache.Default;
        private readonly TimeSpan _expirationTime;
        private readonly object _lock;

        public FhirMemoryCache(string name)
            : this(
                  name,
                  limitSizeInMegabytes: DefaultLimitSizeInMegabytes,
                  expirationTime: TimeSpan.FromMinutes(DefaultExpirationTimeInMinutes))
        {
        }

        public FhirMemoryCache(string name, int limitSizeInMegabytes, TimeSpan expirationTime)
        {
            EnsureArg.IsNotNull(name, nameof(name));
            EnsureArg.IsGt(limitSizeInMegabytes, 0, nameof(name));

            _cache = new MemoryCache(
                name,
                new NameValueCollection()
                {
                    { "CacheMemoryLimitMegabytes", limitSizeInMegabytes.ToString() },
                });

            _expirationTime = expirationTime;

            _lock = new object();
        }

        public long CacheMemoryLimit => ((MemoryCache)_cache).CacheMemoryLimit;

        /// <summary>
        /// Gets or adds the value to cache.
        /// </summary>
        /// <typeparam name="T">Type of the value in cache</typeparam>
        /// <param name="key">Key</param>
        /// <param name="value">Value</param>
        /// <returns>Value in cache</returns>
        public T GetOrAdd(string key, T value)
        {
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
        /// Adds the value to cache if it does not exist.
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="value">Value</param>
        /// <returns>Returns true if the item was added to the cache</returns>
        public bool TryAdd(string key, T value)
        {
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

        public void AddRange(IReadOnlyDictionary<string, T> keyValuePairs)
        {
            foreach (KeyValuePair<string, T> item in keyValuePairs)
            {
                AddInternal(item.Key, item.Value);
            }
        }

        public T Get(string key)
        {
            return (T)_cache[key];
        }

        public T TryGet(string key)
        {
            if (_cache.Contains(key))
            {
                return (T)_cache[key];
            }

            return default;
        }

        public bool TryGet(string key, out T value)
        {
            lock (_lock)
            {
                if (_cache.Contains(key))
                {
                    value = (T)_cache[key];
                    return true;
                }

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
            object objectInCache = _cache.Remove(key);

            return objectInCache != null;
        }

        private DateTimeOffset GetExpirationTime() => DateTimeOffset.Now.Add(_expirationTime);

        private bool AddInternal(string key, T value) => _cache.Add(key, value, GetExpirationTime());
    }
}
