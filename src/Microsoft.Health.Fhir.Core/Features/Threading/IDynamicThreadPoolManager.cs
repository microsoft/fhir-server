// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Threading
{
    /// <summary>
    /// Manages dynamic thread pools that can adapt their thread count at runtime based on system conditions.
    /// </summary>
    public interface IDynamicThreadPoolManager
    {
        /// <summary>
        /// Executes a parallel operation with runtime thread count adaptation.
        /// Thread counts are recalculated periodically during long-running operations.
        /// </summary>
        /// <typeparam name="T">The type of items to process.</typeparam>
        /// <param name="source">The collection of items to process.</param>
        /// <param name="processor">The async function to process each item.</param>
        /// <param name="operationType">The type of operation for thread count calculation.</param>
        /// <param name="recalculationInterval">How often to recalculate thread counts during execution.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task representing the completion of all processing.</returns>
        Task ExecuteAdaptiveParallelAsync<T>(
            IEnumerable<T> source,
            Func<T, CancellationToken, Task> processor,
            OperationType operationType,
            TimeSpan? recalculationInterval = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the current optimal thread count for the specified operation type.
        /// </summary>
        /// <param name="operationType">The type of operation.</param>
        /// <returns>The recommended thread count.</returns>
        int GetCurrentOptimalThreadCount(OperationType operationType);

        /// <summary>
        /// Starts monitoring for a specific operation, enabling runtime adaptation.
        /// </summary>
        /// <param name="operationType">The type of operation to monitor.</param>
        /// <param name="monitoringInterval">How often to check for thread count changes.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A monitoring context that can be used to stop monitoring.</returns>
        Task<IThreadPoolMonitoringContext> StartMonitoringAsync(
            OperationType operationType,
            TimeSpan? monitoringInterval = null,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Represents a monitoring context for thread pool adaptation.
    /// </summary>
    public interface IThreadPoolMonitoringContext : IDisposable
    {
        /// <summary>
        /// Gets the current thread count being used.
        /// </summary>
        int CurrentThreadCount { get; }

        /// <summary>
        /// Gets a value indicating whether monitoring is active.
        /// </summary>
        bool IsMonitoring { get; }

        /// <summary>
        /// Stops the monitoring process.
        /// </summary>
        /// <returns>A task representing the async operation.</returns>
        Task StopMonitoringAsync();
    }
}
