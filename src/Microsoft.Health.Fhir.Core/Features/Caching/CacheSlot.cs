// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Caching
{
    /// <summary>
    /// One cached value with its load lock, expiry, and refresh-failure handling. Shared by
    /// <see cref="GlobalCache{T}"/> (one slot) and <see cref="PartitionCache{T}"/> (one slot per partition).
    /// </summary>
    /// <typeparam name="T">The cached value type.</typeparam>
    [SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "SemaphoreSlim needs disposal only when AvailableWaitHandle is used, which it never is here.")]
    internal sealed class CacheSlot<T>
    {
        private readonly SemaphoreSlim _loadLock = new(1, 1);
        private readonly TimeSpan? _expiry;
        private readonly TimeProvider _timeProvider;
        private Entry _entry;

        public CacheSlot(TimeSpan? expiry, TimeProvider timeProvider)
        {
            if (expiry.HasValue)
            {
                EnsureArg.IsGt(expiry.Value, TimeSpan.Zero, nameof(expiry));
            }

            _expiry = expiry;
            _timeProvider = EnsureArg.IsNotNull(timeProvider, nameof(timeProvider));
        }

        public bool TryGetCurrent(out T value)
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

        public T Load(Func<bool, CancellationToken, ValueTask<T>> load, Action<Exception> onRefreshFailed)
        {
            _loadLock.Wait();
            try
            {
                return TryGetCurrent(out T value)
                    ? value
                    : LoadCoreAsync(load, onRefreshFailed, async: false, CancellationToken.None).EnsureCompleted();
            }
            finally
            {
                _loadLock.Release();
            }
        }

        public async ValueTask<T> LoadAsync(Func<bool, CancellationToken, ValueTask<T>> load, Action<Exception> onRefreshFailed, CancellationToken cancellationToken)
        {
            await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return TryGetCurrent(out T value)
                    ? value
                    : await LoadCoreAsync(load, onRefreshFailed, async: true, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _loadLock.Release();
            }
        }

        private async ValueTask<T> LoadCoreAsync(Func<bool, CancellationToken, ValueTask<T>> load, Action<Exception> onRefreshFailed, bool async, CancellationToken cancellationToken)
        {
            Entry previous = Volatile.Read(ref _entry);
            try
            {
                T value = await load(async, cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref _entry, new Entry(value, _timeProvider.GetUtcNow()));
                return value;
            }
            catch (Exception ex) when (previous != null && !cancellationToken.IsCancellationRequested)
            {
                // Keep serving the previous value; it stays expired, so the next read tries the load again.
                onRefreshFailed(ex);
                return previous.Value;
            }
        }

        private sealed record Entry(T Value, DateTimeOffset LoadedAt);
    }
}
