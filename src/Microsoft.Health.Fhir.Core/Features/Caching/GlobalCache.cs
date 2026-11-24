// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.Core.Features.Caching
{
    /// <summary>
    /// Holds one value of <typeparamref name="T"/> for the whole app, for data that is the same for every partition
    /// (built-in and FHIR service data). Subclasses implement only how the value is loaded.
    /// </summary>
    /// <remarks>
    /// Loads at most once at a time; other callers wait for that load. When a refresh fails, the previous value is
    /// returned and the next read tries again. A failed first load is not cached.
    /// </remarks>
    /// <typeparam name="T">The cached value type. Each cache uses its own type, which is also its DI key.</typeparam>
    public abstract class GlobalCache<T>
    {
        private readonly CacheSlot<T> _slot;
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="GlobalCache{T}"/> class.
        /// </summary>
        /// <param name="logger">The logger used to report failed refreshes.</param>
        /// <param name="expiry">How long a loaded value is used before it is reloaded, or <see langword="null"/> to keep it until the process ends.</param>
        /// <param name="timeProvider">The time provider; defaults to <see cref="TimeProvider.System"/>.</param>
        protected GlobalCache(ILogger logger, TimeSpan? expiry = null, TimeProvider timeProvider = null)
        {
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            _slot = new CacheSlot<T>(expiry, timeProvider ?? TimeProvider.System);
        }

        /// <summary>
        /// Gets the value, loading it with synchronous calls if it is missing or expired.
        /// </summary>
        /// <returns>The cached value.</returns>
        public T Get() => _slot.TryGetCurrent(out T value) ? value : _slot.Load(GetRemoteDataAsync, LogRefreshFailed);

        /// <summary>
        /// Gets the value, loading it asynchronously if it is missing or expired.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The cached value.</returns>
        public ValueTask<T> GetAsync(CancellationToken cancellationToken = default) =>
            _slot.TryGetCurrent(out T value) ? new ValueTask<T>(value) : _slot.LoadAsync(GetRemoteDataAsync, LogRefreshFailed, cancellationToken);

        /// <summary>
        /// Loads the value from its source.
        /// </summary>
        /// <remarks>
        /// When <paramref name="async"/> is <see langword="false"/>, the implementation must use only synchronous
        /// calls so the returned task is already complete; <see cref="Get"/> never blocks on an incomplete task.
        /// </remarks>
        /// <param name="async">Whether to use asynchronous calls.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The loaded value.</returns>
        protected abstract ValueTask<T> GetRemoteDataAsync(bool async, CancellationToken cancellationToken);

        private void LogRefreshFailed(Exception exception) =>
            _logger.LogWarning(exception, "Refreshing {Cache} failed. Using the previous value; the next read tries again.", GetType().Name);
    }
}
