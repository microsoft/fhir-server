// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Caching
{
    /// <summary>
    /// Generic interface for distributed caching of any serializable type
    /// </summary>
    /// <typeparam name="T">The type of objects to cache</typeparam>
    public interface IDistributedCache<T>
        where T : class
    {
        /// <summary>
        /// Gets all cached items
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Collection of all cached items</returns>
        Task<IReadOnlyCollection<T>> GetAllAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets items that were updated after the specified date
        /// </summary>
        /// <param name="lastUpdated">Filter items updated after this date</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Collection of updated items</returns>
        Task<IReadOnlyCollection<T>> GetUpdatedAsync(DateTimeOffset lastUpdated, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a specific item by key
        /// </summary>
        /// <param name="key">The unique key for the item</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The cached item or null if not found</returns>
        Task<T> GetAsync(string key, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets all cached items, replacing any existing cache
        /// </summary>
        /// <param name="items">Collection of items to cache</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task SetAsync(IReadOnlyCollection<T> items, CancellationToken cancellationToken = default);

        /// <summary>
        /// Upserts items in the cache, optionally merging with existing entries
        /// </summary>
        /// <param name="items">Collection of items to cache</param>
        /// <param name="replaceAll">If true, replaces all cached items. If false, merges with existing items.</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task UpsertAsync(IReadOnlyCollection<T> items, bool replaceAll = false, CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes items from the cache by their keys
        /// </summary>
        /// <param name="keys">Collection of keys to remove</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task RemoveAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default);

        /// <summary>
        /// Invalidates all cache entries
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        Task InvalidateAllAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the cache version timestamp
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Cache version timestamp or null if not set</returns>
        Task<DateTimeOffset?> GetCacheVersionAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets the cache version timestamp
        /// </summary>
        /// <param name="version">Version timestamp</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task SetCacheVersionAsync(DateTimeOffset version, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if the cache is available and healthy
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if cache is healthy, false otherwise</returns>
        Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if the cache might be stale based on the cache version timestamp
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if cache appears to be stale, false otherwise</returns>
        Task<bool> IsCacheStaleAsync(CancellationToken cancellationToken = default);
    }
}
