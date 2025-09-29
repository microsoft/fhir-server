// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Storage
{
    /// <summary>
    /// Represents a cache that prioritizes storing and retrieving the most recently accessed items.
    /// </summary>
    /// <typeparam name="T">The type of items to be stored in the cache.</typeparam>
    public class MostRecentlyUsedCache<T>
    {
        private readonly int _capacity;
        private readonly Dictionary<string, T> _cache;

        public MostRecentlyUsedCache(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be greater than zero.");
            }

            _capacity = capacity;
            _cache = new Dictionary<string, T>(capacity);
        }

        public void Add(string key, T item)
        {
            if (_cache.ContainsKey(key))
            {
                _cache[key] = item; // Update existing item
            }
            else
            {
                if (_cache.Count >= _capacity)
                {
                    // Remove the least recently used item (first item in the dictionary)
                    var firstKey = _cache.Keys.First();
                    _cache.Remove(firstKey);
                }

                _cache[key] = item; // Add new item
            }
        }

        public bool Remove(string key)
        {
            return _cache.Remove(key);
        }

        public void Clear()
        {
            _cache.Clear();
        }

        public bool TryGetValue(string key, out T item)
        {
            if (_cache.TryGetValue(key, out item))
            {
                // Move the accessed item to the end to mark it as recently used
                _cache.Remove(key);
                _cache[key] = item;
                return true;
            }

            return false;
        }
    }
}
