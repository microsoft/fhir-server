// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.Core.Features.Search.Registry
{
    /// <summary>
    /// Static manager for search parameter concurrency to prevent race conditions
    /// when multiple requests try to update the same search parameter simultaneously.
    /// </summary>
    public static class SearchParameterConcurrencyManager
    {
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new();
        private static readonly object _cleanupLock = new object();

        /// <summary>
        /// Gets the current number of active locks for debugging/monitoring purposes.
        /// </summary>
        public static int ActiveLockCount => _semaphores.Count;

        /// <summary>
        /// Executes the given function with exclusive access for the specified search parameter URI.
        /// This prevents concurrent modifications to the same search parameter.
        /// </summary>
        /// <typeparam name="T">The return type of the function</typeparam>
        /// <param name="searchParameterUri">The URI of the search parameter to lock on</param>
        /// <param name="function">The function to execute with exclusive access</param>
        /// <param name="logger">Optional logger for debug information</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>The result of the function execution</returns>
        public static async Task<T> ExecuteWithLockAsync<T>(
            string searchParameterUri,
            Func<Task<T>> function,
            ILogger logger = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(searchParameterUri))
            {
                throw new ArgumentException("Search parameter URI cannot be null or empty", nameof(searchParameterUri));
            }

            ArgumentNullException.ThrowIfNull(function);

            var semaphore = _semaphores.GetOrAdd(searchParameterUri, _ => new SemaphoreSlim(1, 1));

            logger?.LogDebug("Acquiring lock for search parameter: {SearchParameterUri}", searchParameterUri);

            await semaphore.WaitAsync(cancellationToken);

            try
            {
                logger?.LogDebug("Lock acquired for search parameter: {SearchParameterUri}", searchParameterUri);
                return await function();
            }
            finally
            {
                semaphore.Release();
                logger?.LogDebug("Lock released for search parameter: {SearchParameterUri}", searchParameterUri);

                // Clean up semaphore if no one is waiting and it's available for immediate use
                // Use a lock to prevent race conditions during cleanup
                lock (_cleanupLock)
                {
                    // Only clean up if the semaphore is available (CurrentCount == 1) and we can successfully remove it
                    if (semaphore.CurrentCount == 1 &&
                        _semaphores.TryRemove(searchParameterUri, out var removedSemaphore))
                    {
                        bool shouldPutBack = false;

                        try
                        {
                            if (ReferenceEquals(removedSemaphore, semaphore))
                            {
                                // Double-check that no other thread acquired the semaphore between our check and removal
                                if (semaphore.CurrentCount == 1)
                                {
                                    logger?.LogDebug("Cleaned up semaphore for search parameter: {SearchParameterUri}", searchParameterUri);

                                    // Don't put back - will dispose in finally
                                }
                                else
                                {
                                    // Put it back if someone acquired it in the meantime
                                    shouldPutBack = true;
                                }
                            }
                            else
                            {
                                // Put it back if it's a different semaphore instance
                                shouldPutBack = true;
                            }

                            if (shouldPutBack)
                            {
                                _semaphores.TryAdd(searchParameterUri, removedSemaphore);
                                removedSemaphore = null; // Prevent dispose in finally since we put it back
                            }
                        }
                        finally
                        {
                            // Only dispose if we didn't put it back into the dictionary
                            removedSemaphore?.Dispose();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Executes the given action with exclusive access for the specified search parameter URI.
        /// This prevents concurrent modifications to the same search parameter.
        /// </summary>
        /// <param name="searchParameterUri">The URI of the search parameter to lock on</param>
        /// <param name="action">The action to execute with exclusive access</param>
        /// <param name="logger">Optional logger for debug information</param>
        /// <param name="cancellationToken">The cancellation token</param>
        public static async Task ExecuteWithLockAsync(
            string searchParameterUri,
            Func<Task> action,
            ILogger logger = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(searchParameterUri))
            {
                throw new ArgumentException("Search parameter URI cannot be null or empty", nameof(searchParameterUri));
            }

            ArgumentNullException.ThrowIfNull(action);

            await ExecuteWithLockAsync(
                searchParameterUri,
                async () =>
                {
                    await action();
                    return 0; // Return dummy value for action overload
                },
                logger,
                cancellationToken);
        }
    }
}
