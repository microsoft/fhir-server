// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.Core.Features.Caching
{
    /// <summary>
    /// Holds one value of <typeparamref name="T"/> per partition, keyed by partition ID. Subclasses implement only
    /// how the value is loaded.
    /// </summary>
    /// <remarks>
    /// A <see langword="null"/> partition ID is the unpartitioned value. Each partition loads at most once at a time;
    /// other callers wait for that load. When a refresh fails, the previous value is returned and the next read tries
    /// again. A failed first load is not cached.
    /// </remarks>
    /// <typeparam name="T">The cached value type. Each cache uses its own type, which is also its DI key.</typeparam>
    public abstract class PartitionCache<T>
    {
        private readonly ConcurrentDictionary<string, CacheSlot<T>> _slots = new(StringComparer.Ordinal);
        private readonly ILogger _logger;
        private readonly TimeSpan? _expiry;
        private readonly TimeProvider _timeProvider;

        /// <summary>
        /// Initializes a new instance of the <see cref="PartitionCache{T}"/> class.
        /// </summary>
        /// <param name="logger">The logger used to report failed refreshes.</param>
        /// <param name="expiry">How long a loaded value is used before it is reloaded, or <see langword="null"/> to keep it until the process ends.</param>
        /// <param name="timeProvider">The time provider; defaults to <see cref="TimeProvider.System"/>.</param>
        protected PartitionCache(ILogger logger, TimeSpan? expiry = null, TimeProvider timeProvider = null)
        {
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            if (expiry.HasValue)
            {
                EnsureArg.IsGt(expiry.Value, TimeSpan.Zero, nameof(expiry));
            }

            _expiry = expiry;
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <summary>
        /// Gets the value for a partition, loading it with synchronous calls if it is missing or expired.
        /// </summary>
        /// <param name="partitionId">The partition ID, or <see langword="null"/> for the unpartitioned value.</param>
        /// <returns>The cached value.</returns>
        public T Get(string partitionId)
        {
            CacheSlot<T> slot = GetSlot(partitionId);
            return slot.TryGetCurrent(out T value)
                ? value
                : slot.Load((async, cancellationToken) => GetRemoteDataAsync(partitionId, async, cancellationToken), ex => LogRefreshFailed(ex, partitionId));
        }

        /// <summary>
        /// Gets the value for a partition, loading it asynchronously if it is missing or expired.
        /// </summary>
        /// <param name="partitionId">The partition ID, or <see langword="null"/> for the unpartitioned value.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The cached value.</returns>
        public ValueTask<T> GetAsync(string partitionId, CancellationToken cancellationToken = default)
        {
            CacheSlot<T> slot = GetSlot(partitionId);
            return slot.TryGetCurrent(out T value)
                ? new ValueTask<T>(value)
                : slot.LoadAsync((async, ct) => GetRemoteDataAsync(partitionId, async, ct), ex => LogRefreshFailed(ex, partitionId), cancellationToken);
        }

        /// <summary>
        /// Loads the value for a partition from its source.
        /// </summary>
        /// <remarks>
        /// When <paramref name="async"/> is <see langword="false"/>, the implementation must use only synchronous
        /// calls so the returned task is already complete; <see cref="Get"/> never blocks on an incomplete task.
        /// </remarks>
        /// <param name="partitionId">The partition ID, or <see langword="null"/> for the unpartitioned value.</param>
        /// <param name="async">Whether to use asynchronous calls.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The loaded value.</returns>
        protected abstract ValueTask<T> GetRemoteDataAsync(string partitionId, bool async, CancellationToken cancellationToken);

        private CacheSlot<T> GetSlot(string partitionId) =>
            _slots.GetOrAdd(partitionId ?? string.Empty, _ => new CacheSlot<T>(_expiry, _timeProvider));

        private void LogRefreshFailed(Exception exception, string partitionId) =>
            _logger.LogWarning(exception, "Refreshing {Cache} for partition '{PartitionId}' failed. Using the previous value; the next read tries again.", GetType().Name, partitionId);
    }
}
