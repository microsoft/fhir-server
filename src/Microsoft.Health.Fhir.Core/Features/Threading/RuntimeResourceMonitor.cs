// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.Core.Features.Threading
{
    /// <summary>
    /// Monitors runtime system resources for dynamic threading decisions.
    /// </summary>
    public sealed class RuntimeResourceMonitor : IRuntimeResourceMonitor
    {
        private readonly ILogger<RuntimeResourceMonitor> _logger;
        private readonly DateTime _startTime;
        private readonly Process _currentProcess;

        public RuntimeResourceMonitor(ILogger<RuntimeResourceMonitor> logger)
        {
            _logger = logger;
            _startTime = DateTime.UtcNow;
            _currentProcess = Process.GetCurrentProcess();

            _logger.LogInformation("RuntimeResourceMonitor initialized with cross-platform resource monitoring");
        }

        public int GetCurrentProcessorCount()
        {
            // This can change in containerized environments
            return Environment.ProcessorCount;
        }

        public long GetCurrentAvailableMemoryMB()
        {
            try
            {
                // Use cross-platform approach with GC and Process information
                var processMemoryMB = _currentProcess.WorkingSet64 / (1024 * 1024);
                var gcMemoryMB = GC.GetTotalMemory(false) / (1024 * 1024);

                // For containerized environments, try to get container memory limits
                var containerMemoryLimitMB = GetContainerMemoryLimitMB();
                if (containerMemoryLimitMB > 0)
                {
                    // In containers, available = limit - current usage
                    return Math.Max(0, containerMemoryLimitMB - processMemoryMB);
                }

                // Fallback: estimate based on typical server configurations
                // This is a rough estimate - in production you'd want more sophisticated monitoring
                var estimatedTotalMemoryMB = Math.Max(4096, processMemoryMB * 4); // Assume process uses ~25% of total
                return Math.Max(1024, estimatedTotalMemoryMB - processMemoryMB);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to get available memory, using conservative fallback");
                return 2048; // Conservative 2GB fallback
            }
        }

        public double GetCurrentMemoryUsagePercentage()
        {
            try
            {
                var availableMemory = GetCurrentAvailableMemoryMB();
                var processMemory = _currentProcess.WorkingSet64 / (1024 * 1024);

                // Rough calculation - in production you'd want more sophisticated monitoring
                return Math.Min(100.0, (double)processMemory / (availableMemory + processMemory) * 100);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to calculate memory usage percentage");
                return 0; // Assume low usage if we can't determine
            }
        }

        public bool IsUnderResourcePressure()
        {
            try
            {
                var memoryUsage = GetCurrentMemoryUsagePercentage();
                var cpuUsage = GetCurrentCpuUsagePercentage();

                // Consider system under pressure if memory > 80% or CPU > 90%
                var underPressure = memoryUsage > 80 || cpuUsage > 90;

                _logger.LogDebug(
                    "Resource pressure check: Memory={MemoryUsage:F1}%, CPU={CpuUsage:F1}%, UnderPressure={UnderPressure}",
                    memoryUsage,
                    cpuUsage,
                    underPressure);

                return underPressure;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to determine resource pressure, assuming not under pressure");
                return false;
            }
        }

        private long GetContainerMemoryLimitMB()
        {
            try
            {
                // Try to read container memory limit from cgroup (Linux containers)
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    if (System.IO.File.Exists("/sys/fs/cgroup/memory/memory.limit_in_bytes"))
                    {
                        var limitBytes = System.IO.File.ReadAllText("/sys/fs/cgroup/memory/memory.limit_in_bytes").Trim();
                        if (long.TryParse(limitBytes, out var limit) && limit > 0 && limit < long.MaxValue)
                        {
                            return limit / (1024 * 1024); // Convert to MB
                        }
                    }
                }

                // Could add Windows container support here if needed
                return 0; // No container limit detected
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to read container memory limit");
                return 0;
            }
        }

        private double GetCurrentCpuUsagePercentage()
        {
            try
            {
                // Simple CPU usage estimation based on process CPU time
                var currentTime = DateTime.UtcNow;
                var totalElapsedTime = currentTime - _startTime;
                var processorTime = _currentProcess.TotalProcessorTime;

                // Rough estimate: (total CPU time / (elapsed time * processor count)) * 100
                if (totalElapsedTime.TotalMilliseconds > 1000) // Avoid division by very small numbers
                {
                    var cpuUsage = (processorTime.TotalMilliseconds / (totalElapsedTime.TotalMilliseconds * Environment.ProcessorCount)) * 100;
                    return Math.Min(100.0, Math.Max(0.0, cpuUsage));
                }

                return 0; // Return 0 if we don't have enough data yet
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to calculate CPU usage");
                return 0;
            }
        }

        private long GetFallbackAvailableMemory()
        {
            // Fallback: estimate based on GC and process memory
            var gcMemory = GC.GetTotalMemory(false) / (1024 * 1024);
            var processMemory = _currentProcess.WorkingSet64 / (1024 * 1024);

            // Very rough estimate - assume 4GB total memory as baseline
            return Math.Max(1024, 4096 - processMemory);
        }

        public void Dispose()
        {
            _currentProcess?.Dispose();
        }
    }
}
