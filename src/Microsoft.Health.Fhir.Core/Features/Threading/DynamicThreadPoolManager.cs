// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.Core.Features.Threading
{
    /// <summary>
    /// Manages dynamic thread pools that can adapt their thread count at runtime based on system conditions.
    /// This service enables long-running operations to adjust their parallelism as system resources change.
    /// </summary>
    public class DynamicThreadPoolManager : IDynamicThreadPoolManager
    {
        private readonly IDynamicThreadingService _dynamicThreadingService;
        private readonly IRuntimeResourceMonitor _resourceMonitor;
        private readonly ILogger<DynamicThreadPoolManager> _logger;
        private readonly ConcurrentDictionary<string, MonitoringContext> _activeMonitors = new();

        public DynamicThreadPoolManager(
            IDynamicThreadingService dynamicThreadingService,
            IRuntimeResourceMonitor resourceMonitor,
            ILogger<DynamicThreadPoolManager> logger)
        {
            _dynamicThreadingService = dynamicThreadingService ?? throw new ArgumentNullException(nameof(dynamicThreadingService));
            _resourceMonitor = resourceMonitor ?? throw new ArgumentNullException(nameof(resourceMonitor));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Executes a parallel operation with runtime thread count adaptation.
        /// </summary>
        /// <typeparam name="T">The type of items to process.</typeparam>
        /// <param name="source">The collection of items to process.</param>
        /// <param name="processor">The async function to process each item.</param>
        /// <param name="operationType">The type of operation for thread count calculation.</param>
        /// <param name="recalculationInterval">How often to recalculate thread counts during execution.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task representing the completion of all processing.</returns>
        public async Task ExecuteAdaptiveParallelAsync<T>(
            IEnumerable<T> source,
            Func<T, CancellationToken, Task> processor,
            OperationType operationType,
            TimeSpan? recalculationInterval = null,
            CancellationToken cancellationToken = default)
        {
            var interval = recalculationInterval ?? TimeSpan.FromSeconds(30);
            var sourceList = source.ToList();

            if (!sourceList.Any())
            {
                _logger.LogDebug("No items to process for {OperationType}", operationType);
                return;
            }

            var initialThreadCount = _dynamicThreadingService.GetOptimalThreadCount(operationType);
            _logger.LogInformation(
                "Starting adaptive parallel execution for {OperationType} with {ItemCount} items, initial threads: {ThreadCount}, recalculation interval: {Interval}",
                operationType,
                sourceList.Count,
                initialThreadCount,
                interval);

            using var semaphore = new SemaphoreSlim(initialThreadCount, initialThreadCount);
            using var recalculationTimer = new Timer(RecalculateThreads, (semaphore, operationType), interval, interval);

            var startTime = DateTime.UtcNow;
            var completedItems = 0;

            try
            {
                await Parallel.ForEachAsync(
                    sourceList,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = initialThreadCount,
                        CancellationToken = cancellationToken,
                    },
                    async (item, ct) =>
                    {
                        await semaphore.WaitAsync(ct);
                        try
                        {
                            await processor(item, ct);
                            var completed = Interlocked.Increment(ref completedItems);

                            if (completed % 100 == 0) // Log progress every 100 items
                            {
                                var elapsed = DateTime.UtcNow - startTime;
                                var rate = completed / elapsed.TotalMinutes;
                                _logger.LogDebug(
                                    "Processed {Completed}/{Total} items for {OperationType} (rate: {Rate:F2}/min, threads: {ThreadCount})",
                                    completed,
                                    sourceList.Count,
                                    operationType,
                                    rate,
                                    semaphore.CurrentCount);
                            }
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    });

                var totalElapsed = DateTime.UtcNow - startTime;
                _logger.LogInformation(
                    "Completed adaptive parallel execution for {OperationType}: {ItemCount} items in {Duration:F2}s (rate: {Rate:F2}/min)",
                    operationType,
                    sourceList.Count,
                    totalElapsed.TotalSeconds,
                    completedItems / totalElapsed.TotalMinutes);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error during adaptive parallel execution for {OperationType} after processing {CompletedItems}/{TotalItems} items",
                    operationType,
                    completedItems,
                    sourceList.Count);
                throw;
            }
        }

        /// <summary>
        /// Gets the current optimal thread count for the specified operation type.
        /// </summary>
        /// <param name="operationType">The type of operation.</param>
        /// <returns>The recommended thread count.</returns>
        public int GetCurrentOptimalThreadCount(OperationType operationType)
        {
            return _dynamicThreadingService.GetOptimalThreadCount(operationType);
        }

        /// <summary>
        /// Starts monitoring for a specific operation, enabling runtime adaptation.
        /// </summary>
        /// <param name="operationType">The type of operation to monitor.</param>
        /// <param name="monitoringInterval">How often to check for thread count changes.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A monitoring context that can be used to stop monitoring.</returns>
        public async Task<IThreadPoolMonitoringContext> StartMonitoringAsync(
            OperationType operationType,
            TimeSpan? monitoringInterval = null,
            CancellationToken cancellationToken = default)
        {
            var interval = monitoringInterval ?? TimeSpan.FromMinutes(2);
            var monitoringId = Guid.NewGuid().ToString();

            var context = new MonitoringContext(
                monitoringId,
                operationType,
                _dynamicThreadingService.GetOptimalThreadCount(operationType),
                _logger);

            _activeMonitors[monitoringId] = context;

            // Start the monitoring timer
            context.StartMonitoring(interval, () =>
            {
                var newThreadCount = _dynamicThreadingService.GetOptimalThreadCount(operationType);
                context.UpdateThreadCount(newThreadCount);
            });

            _logger.LogInformation(
                "Started thread pool monitoring for {OperationType} with interval {Interval} (monitoring ID: {MonitoringId})",
                operationType,
                interval,
                monitoringId);

            await Task.CompletedTask;
            return context;
        }

        private void RecalculateThreads(object state)
        {
            if (state is not ValueTuple<SemaphoreSlim, OperationType> tuple)
            {
                return;
            }

            var (semaphore, operationType) = tuple;

            try
            {
                var currentThreadCount = semaphore.CurrentCount;
                var optimalThreadCount = _dynamicThreadingService.GetOptimalThreadCount(operationType);

                if (optimalThreadCount != currentThreadCount)
                {
                    AdjustSemaphore(semaphore, currentThreadCount, optimalThreadCount, operationType);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during thread count recalculation for {OperationType}", operationType);
            }
        }

        private void AdjustSemaphore(SemaphoreSlim semaphore, int currentCount, int targetCount, OperationType operationType)
        {
            var difference = targetCount - currentCount;

            if (difference > 0)
            {
                // Increase thread count
                semaphore.Release(difference);
                _logger.LogInformation(
                    "Increased thread count for {OperationType}: {CurrentCount} -> {TargetCount} (+{Difference})",
                    operationType,
                    currentCount,
                    targetCount,
                    difference);
            }
            else if (difference < 0)
            {
                // Decrease thread count by waiting for the excess permits
                var decreaseCount = Math.Abs(difference);
                for (int i = 0; i < decreaseCount; i++)
                {
                    if (semaphore.Wait(0)) // Non-blocking wait
                    {
                        // Successfully acquired a permit, effectively reducing the available count
                    }
                }

                _logger.LogInformation(
                    "Decreased thread count for {OperationType}: {CurrentCount} -> {TargetCount} ({Difference})",
                    operationType,
                    currentCount,
                    targetCount,
                    difference);
            }
        }

        /// <summary>
        /// Internal monitoring context implementation.
        /// </summary>
        private class MonitoringContext : IThreadPoolMonitoringContext
        {
            private readonly string _monitoringId;
            private readonly OperationType _operationType;
            private readonly ILogger _logger;
            private Timer _monitoringTimer;
            private volatile bool _isMonitoring = true;
            private volatile int _currentThreadCount;
            private bool _disposed;

            public MonitoringContext(
                string monitoringId,
                OperationType operationType,
                int initialThreadCount,
                ILogger logger)
            {
                _monitoringId = monitoringId;
                _operationType = operationType;
                _currentThreadCount = initialThreadCount;
                _logger = logger;
            }

            public int CurrentThreadCount => _currentThreadCount;

            public bool IsMonitoring => _isMonitoring && !_disposed;

            public void StartMonitoring(TimeSpan interval, Action recalculateAction)
            {
                _monitoringTimer = new Timer(
                    _ =>
                {
                    if (_isMonitoring && !_disposed)
                    {
                        recalculateAction();
                    }
                },
                    null,
                    interval,
                    interval);
            }

            public void UpdateThreadCount(int newThreadCount)
            {
                var previousCount = _currentThreadCount;
                _currentThreadCount = newThreadCount;

                if (previousCount != newThreadCount)
                {
                    _logger.LogDebug(
                        "Thread count updated for {OperationType} monitoring {MonitoringId}: {PreviousCount} -> {NewCount}",
                        _operationType,
                        _monitoringId,
                        previousCount,
                        newThreadCount);
                }
            }

            public async Task StopMonitoringAsync()
            {
                _isMonitoring = false;
                if (_monitoringTimer != null)
                {
                    await _monitoringTimer.DisposeAsync();
                }

                _logger.LogInformation(
                    "Stopped thread pool monitoring for {OperationType} (monitoring ID: {MonitoringId})",
                    _operationType,
                    _monitoringId);
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    _disposed = true;
                    _isMonitoring = false;
                    _monitoringTimer?.Dispose();
                }
            }
        }
    }
}
