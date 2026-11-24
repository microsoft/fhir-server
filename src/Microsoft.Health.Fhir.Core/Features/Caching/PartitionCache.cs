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
using Microsoft.Health.Fhir.Core.Features.Partitions;

namespace Microsoft.Health.Fhir.Core.Features.Caching
{
    /// <summary>
    /// Holds one value of <typeparamref name="T"/> per partition. Subclasses implement only how the value is loaded.
    /// </summary>
    /// <remarks>
    /// A <see langword="null"/> partition is the unpartitioned value, matching
    /// <see cref="Context.IFhirRequestContext.Partition"/>. Each partition loads at most once at a time; other
    /// callers wait for that load. When a refresh fails and a previous value exists, the previous value is
    /// kept for another expiry interval before the next attempt. A failed first load is not cached.
    /// </remarks>
    /// <typeparam name="T">The cached value type. Each cache uses its own type, which is also its DI key.</typeparam>
    public abstract class PartitionCache<T>
    {
        private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _loadLocks = new(StringComparer.Ordinal);
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
        /// <param name="partition">The partition, or <see langword="null"/> for the unpartitioned value.</param>
        /// <returns>The cached value.</returns>
        public T Get(IPartitionInfo partition)
        {
            string key = GetKey(partition);
            if (TryGetCurrent(key, out T value))
            {
                return value;
            }

            SemaphoreSlim loadLock = GetLoadLock(key);
            loadLock.Wait();
            try
            {
                return TryGetCurrent(key, out value)
                    ? value
                    : LoadAsync(partition, key, async: false, CancellationToken.None).EnsureCompleted();
            }
            finally
            {
                loadLock.Release();
            }
        }

        /// <summary>
        /// Gets the value for a partition, loading it asynchronously if it is missing or expired.
        /// </summary>
        /// <param name="partition">The partition, or <see langword="null"/> for the unpartitioned value.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The cached value.</returns>
        public async ValueTask<T> GetAsync(IPartitionInfo partition, CancellationToken cancellationToken = default)
        {
            string key = GetKey(partition);
            if (TryGetCurrent(key, out T value))
            {
                return value;
            }

            SemaphoreSlim loadLock = GetLoadLock(key);
            await loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return TryGetCurrent(key, out value)
                    ? value
                    : await LoadAsync(partition, key, async: true, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                loadLock.Release();
            }
        }

        /// <summary>
        /// Loads the value for a partition from its source.
        /// </summary>
        /// <remarks>
        /// When <paramref name="async"/> is <see langword="false"/>, the implementation must use only synchronous
        /// calls so the returned task is already complete; <see cref="Get"/> never blocks on an incomplete task.
        /// </remarks>
        /// <param name="partition">The partition, or <see langword="null"/> for the unpartitioned value.</param>
        /// <param name="async">Whether to use asynchronous calls.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The loaded value.</returns>
        protected abstract ValueTask<T> GetRemoteDataAsync(IPartitionInfo partition, bool async, CancellationToken cancellationToken);

        private static string GetKey(IPartitionInfo partition) => partition?.Id ?? string.Empty;

        private bool TryGetCurrent(string key, out T value)
        {
            if (_entries.TryGetValue(key, out Entry entry) && !IsExpired(entry))
            {
                value = entry.Value;
                return true;
            }

            value = default;
            return false;
        }

        private bool IsExpired(Entry entry) => _expiry.HasValue && _timeProvider.GetUtcNow() - entry.LoadedAt >= _expiry.Value;

        private SemaphoreSlim GetLoadLock(string key) => _loadLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

        private async ValueTask<T> LoadAsync(IPartitionInfo partition, string key, bool async, CancellationToken cancellationToken)
        {
            try
            {
                T value = await GetRemoteDataAsync(partition, async, cancellationToken).ConfigureAwait(false);
                _entries[key] = new Entry(value, _timeProvider.GetUtcNow());
                return value;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && _entries.TryGetValue(key, out Entry previous))
            {
                // Keep the previous value for another interval so waiting readers don't each retry the failing load.
                _entries[key] = previous with { LoadedAt = _timeProvider.GetUtcNow() };
                _logger.LogWarning(ex, "Refreshing {Cache} for partition '{PartitionId}' failed. Using the previous value until the next refresh.", GetType().Name, partition?.Id);
                return previous.Value;
            }
        }

        private sealed record Entry(T Value, DateTimeOffset LoadedAt);
    }
}
