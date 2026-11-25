// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Extensions;

namespace Microsoft.Health.Fhir.Core.Features.Caching
{
    /// <summary>
    /// One cached value with its loader, load lock, expiry, and refresh-failure handling. Shared by
    /// <see cref="GlobalCache{T}"/> (one slot) and <see cref="PartitionCache{T}"/> (one slot per partition).
    /// </summary>
    /// <typeparam name="T">The cached value type.</typeparam>
    [SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "SemaphoreSlim needs disposal only when AvailableWaitHandle is used, which it never is here.")]
    internal sealed class CacheSlot<T>
    {
        private readonly SemaphoreSlim _loadLock = new(1, 1);
        private readonly Func<bool, CancellationToken, ValueTask<T>> _load;
        private readonly Action<Exception> _onRefreshFailed;
        private readonly TimeSpan? _expiry;
        private readonly TimeProvider _timeProvider;
        private Entry _entry;

        /// <summary>
        /// Initializes a new instance of the <see cref="CacheSlot{T}"/> class.
        /// </summary>
        /// <param name="load">Loads the value; the first argument is whether to use asynchronous calls.</param>
        /// <param name="onRefreshFailed">Called when a refresh fails and the previous value is returned instead.</param>
        /// <param name="expiry">How long a loaded value is used, or <see langword="null"/> to keep it.</param>
        /// <param name="timeProvider">The time provider.</param>
        public CacheSlot(Func<bool, CancellationToken, ValueTask<T>> load, Action<Exception> onRefreshFailed, TimeSpan? expiry, TimeProvider timeProvider)
        {
            if (expiry.HasValue)
            {
                EnsureArg.IsGt(expiry.Value, TimeSpan.Zero, nameof(expiry));
            }

            _load = EnsureArg.IsNotNull(load, nameof(load));
            _onRefreshFailed = EnsureArg.IsNotNull(onRefreshFailed, nameof(onRefreshFailed));
            _expiry = expiry;
            _timeProvider = EnsureArg.IsNotNull(timeProvider, nameof(timeProvider));
        }

        /// <summary>
        /// Gets the current value, or loads it with synchronous calls if it is missing or expired.
        /// </summary>
        public T GetOrUpdate()
        {
            if (TryGetCurrent(out T value))
            {
                return value;
            }

            _loadLock.Wait();
            try
            {
                return TryGetCurrent(out value)
                    ? value
                    : UpdateAsync(async: false, CancellationToken.None).EnsureCompleted();
            }
            finally
            {
                _loadLock.Release();
            }
        }

        /// <summary>
        /// Gets the current value, or loads it asynchronously if it is missing or expired.
        /// </summary>
        public ValueTask<T> GetOrUpdateAsync(CancellationToken cancellationToken) =>
            TryGetCurrent(out T value) ? new ValueTask<T>(value) : LockAndUpdateAsync(cancellationToken);

        private async ValueTask<T> LockAndUpdateAsync(CancellationToken cancellationToken)
        {
            await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return TryGetCurrent(out T value)
                    ? value
                    : await UpdateAsync(async: true, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _loadLock.Release();
            }
        }

        private bool TryGetCurrent(out T value)
        {
            Entry entry = Volatile.Read(ref _entry);
            if (entry != null && !(_expiry.HasValue && _timeProvider.GetUtcNow() - entry.LoadedAt >= _expiry.Value))
            {
                value = entry.Value;
                return true;
            }

            value = default;
            return false;
        }

        private async ValueTask<T> UpdateAsync(bool async, CancellationToken cancellationToken)
        {
            Entry previous = Volatile.Read(ref _entry);
            try
            {
                ValueTask<T> pending = _load(async, cancellationToken);
                if (async || pending.IsCompleted)
                {
                    T value = await pending.ConfigureAwait(false);
                    Volatile.Write(ref _entry, new Entry(value, _timeProvider.GetUtcNow()));
                    return value;
                }
            }
            catch (Exception ex) when (previous != null && !cancellationToken.IsCancellationRequested)
            {
                // Keep serving the previous value; it stays expired, so the next read tries the load again.
                _onRefreshFailed(ex);
                return previous.Value;
            }

            // Not awaited, so the rejected load can never publish its value later, outside the lock.
            throw new InvalidOperationException("A synchronous load returned an incomplete task. Loaders must not await when async is false.");
        }

        private sealed record Entry(T Value, DateTimeOffset LoadedAt);
    }
}
