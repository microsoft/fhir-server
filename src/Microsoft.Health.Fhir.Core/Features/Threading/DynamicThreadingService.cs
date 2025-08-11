// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.Core.Features.Threading
{
    /// <summary>
    /// Service to provide dynamic threading configurations based on system resources.
    /// </summary>
    public class DynamicThreadingService : IDynamicThreadingService
    {
        private readonly ILogger<DynamicThreadingService> _logger;
        private readonly IRuntimeResourceMonitor _resourceMonitor;

        public DynamicThreadingService(
            ILogger<DynamicThreadingService> logger,
            IRuntimeResourceMonitor resourceMonitor)
        {
            _logger = logger;
            _resourceMonitor = resourceMonitor;

            _logger.LogInformation(
                "DynamicThreadingService initialized with runtime resource monitoring");
        }

        /// <summary>
        /// Gets the optimal thread count for the specified operation type.
        /// </summary>
        public int GetOptimalThreadCount(OperationType operationType)
        {
            try
            {
                // Get current system state
                var currentProcessorCount = _resourceMonitor.GetCurrentProcessorCount();
                var isUnderPressure = _resourceMonitor.IsUnderResourcePressure();

                // Check SQL-specific pressure if we have SQL monitoring
                var isSqlUnderPressure = false;
                if (_resourceMonitor is ISqlResourceMonitor sqlMonitor)
                {
                    try
                    {
                        isSqlUnderPressure = sqlMonitor.IsSqlUnderPressureAsync().GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to check SQL pressure, continuing with system pressure only");
                    }
                }

                // Combine system and SQL pressure
                var combinedPressure = isUnderPressure || isSqlUnderPressure;

                // More aggressive reduction if SQL is under pressure (database operations are bottlenecked)
                var pressureMultiplier = combinedPressure ? (isSqlUnderPressure ? 0.4 : 0.7) : 1.0;

                var baseThreads = operationType switch
                {
                    // Export operations are primarily I/O bound, can handle more threads
                    OperationType.Export => Math.Min(Math.Max(currentProcessorCount, 4), 8),

                    // Import operations are more resource intensive, use fewer threads
                    OperationType.Import => Math.Min(Math.Max(currentProcessorCount / 2, 2), 6),

                    // Bulk update operations balance between I/O and CPU
                    OperationType.BulkUpdate => Math.Min(Math.Max(currentProcessorCount, 3), 6),

                    // Index rebuild is CPU intensive
                    OperationType.IndexRebuild => Math.Min(Math.Max(currentProcessorCount / 3, 1), 4),

                    _ => Math.Min(currentProcessorCount, 4),
                };

                var adjustedThreads = Math.Max(1, (int)(baseThreads * pressureMultiplier));

                _logger.LogDebug(
                    "Calculated optimal threads for {OperationType}: Base={BaseThreads}, SystemPressure={SystemPressure}, SqlPressure={SqlPressure}, Final={FinalThreads}, ProcessorCount={ProcessorCount}",
                    operationType,
                    baseThreads,
                    isUnderPressure,
                    isSqlUnderPressure,
                    adjustedThreads,
                    currentProcessorCount);

                return adjustedThreads;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to calculate optimal thread count for {OperationType}, using fallback", operationType);
                return GetFallbackThreadCount(operationType);
            }
        }

        /// <summary>
        /// Gets the maximum concurrent operations for the specified type.
        /// </summary>
        public int GetMaxConcurrentOperations(OperationType operationType)
        {
            var baseCount = GetOptimalThreadCount(operationType);

            return operationType switch
            {
                OperationType.Export => Math.Max(baseCount / 2, 1),
                OperationType.Import => Math.Max(baseCount / 3, 1),
                OperationType.BulkUpdate => Math.Max(baseCount / 2, 1),
                OperationType.IndexRebuild => 1, // Keep conservative for index operations
                _ => Math.Max(baseCount / 2, 1),
            };
        }

        /// <summary>
        /// Gets a fallback thread count when dynamic calculation fails.
        /// </summary>
        private static int GetFallbackThreadCount(OperationType operationType)
        {
            return operationType switch
            {
                OperationType.Export => 4,
                OperationType.Import => 2,
                OperationType.BulkUpdate => 3,
                OperationType.IndexRebuild => 1,
                _ => 2,
            };
        }

        /// <summary>
        /// Checks if the system has sufficient resources for the operation.
        /// </summary>
        public bool HasSufficientResources(
            OperationType operationType,
            int requestedThreads)
        {
            var currentProcessorCount = _resourceMonitor.GetCurrentProcessorCount();
            var availableMemoryMB = _resourceMonitor.GetCurrentAvailableMemoryMB();
            var memoryUsagePercent = _resourceMonitor.GetCurrentMemoryUsagePercentage();
            var isUnderPressure = _resourceMonitor.IsUnderResourcePressure();

            var optimalThreads = GetOptimalThreadCount(operationType);
            var memoryPerThread = availableMemoryMB / currentProcessorCount;

            // More sophisticated resource checking
            var hasEnoughThreads = requestedThreads <= optimalThreads;
            var hasEnoughMemory = memoryPerThread > 50 && memoryUsagePercent < 85;
            var notUnderPressure = !isUnderPressure;

            var sufficient = hasEnoughThreads && hasEnoughMemory && notUnderPressure;

            _logger.LogDebug(
                "Resource check for {OperationType}: RequestedThreads={RequestedThreads}, OptimalThreads={OptimalThreads}, MemoryPerThread={MemoryPerThread}MB, MemoryUsage={MemoryUsage:F1}%, UnderPressure={UnderPressure}, Sufficient={Sufficient}",
                operationType,
                requestedThreads,
                optimalThreads,
                memoryPerThread,
                memoryUsagePercent,
                isUnderPressure,
                sufficient);

            return sufficient;
        }
    }
}
