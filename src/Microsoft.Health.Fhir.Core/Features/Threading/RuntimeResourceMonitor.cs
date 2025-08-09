// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.Core.Features.Threading
{
    /// <summary>
    /// Monitors runtime system resources for dynamic threading decisions.
    /// Provides accurate system-wide resource monitoring similar to Task Manager.
    /// </summary>
    public sealed class RuntimeResourceMonitor : IRuntimeResourceMonitor
    {
        private static readonly string[] SplitSeparators = { " ", "\t" };

        private readonly ILogger<RuntimeResourceMonitor> _logger;
        private readonly Process _currentProcess;
        private readonly bool _isWindows;
        private readonly bool _isLinux;
        private readonly object _cpuLock = new object();
        private readonly PerformanceCounter _systemCpuCounter;
        private readonly PerformanceCounter _systemMemoryCounter;

        private DateTime _lastCpuTime = DateTime.UtcNow;
        private TimeSpan _lastTotalProcessorTime;

        public RuntimeResourceMonitor(ILogger<RuntimeResourceMonitor> logger)
        {
            _logger = logger;
            _currentProcess = Process.GetCurrentProcess();
            _isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            _isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

            // Initialize Windows performance counters if available
            if (_isWindows)
            {
#pragma warning disable CA1416 // Windows-specific code
                try
                {
                    _systemCpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                    _systemMemoryCounter = new PerformanceCounter("Memory", "Available MBytes");

                    // Prime the CPU counter (first call returns 0)
                    _systemCpuCounter.NextValue();

                    _logger.LogInformation("RuntimeResourceMonitor initialized with Windows Performance Counters");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to initialize Windows Performance Counters, falling back to cross-platform monitoring");
                    _systemCpuCounter?.Dispose();
                    _systemMemoryCounter?.Dispose();
                    _systemCpuCounter = null;
                    _systemMemoryCounter = null;
                }
#pragma warning restore CA1416
            }
            else
            {
                _logger.LogInformation("RuntimeResourceMonitor initialized with cross-platform monitoring for {Platform}", _isLinux ? "Linux" : "Other");
            }

            // Initialize baseline for cross-platform CPU monitoring
            _lastTotalProcessorTime = _currentProcess.TotalProcessorTime;
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
                if (_isWindows && _systemMemoryCounter != null)
                {
#pragma warning disable CA1416 // Windows-specific code
                    // Use Windows Performance Counter for accurate available memory
                    return (long)_systemMemoryCounter.NextValue();
#pragma warning restore CA1416
                }

                if (_isLinux)
                {
                    // Read from /proc/meminfo on Linux
                    return GetLinuxAvailableMemoryMB();
                }

                // Fallback for other platforms
                return GetFallbackAvailableMemoryMB();
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
                if (_isWindows && _systemMemoryCounter != null)
                {
#pragma warning disable CA1416 // Windows-specific code
                    // Get total physical memory and available memory
                    var availableMemoryMB = (long)_systemMemoryCounter.NextValue();
                    var totalMemoryMB = GetTotalPhysicalMemoryMB();

                    if (totalMemoryMB > 0)
                    {
                        var usedMemoryMB = totalMemoryMB - availableMemoryMB;
                        return Math.Max(0.0, Math.Min(100.0, (double)usedMemoryMB / totalMemoryMB * 100));
                    }
#pragma warning restore CA1416
                }

                if (_isLinux)
                {
                    return GetLinuxMemoryUsagePercentage();
                }

                // Fallback calculation
                return GetFallbackMemoryUsagePercentage();
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

                // Consider system under pressure if memory > 80% or CPU > 85%
                var underPressure = memoryUsage > 80 || cpuUsage > 85;

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

        public void Dispose()
        {
            _systemCpuCounter?.Dispose();
            _systemMemoryCounter?.Dispose();
            _currentProcess?.Dispose();
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        private double GetCurrentCpuUsagePercentage()
        {
            try
            {
                if (_isWindows && _systemCpuCounter != null)
                {
#pragma warning disable CA1416 // Windows-specific code
                    // Use Windows Performance Counter for accurate system-wide CPU usage
                    var cpuUsage = _systemCpuCounter.NextValue();
                    return Math.Max(0.0, Math.Min(100.0, cpuUsage));
#pragma warning restore CA1416
                }

                if (_isLinux)
                {
                    return GetLinuxCpuUsagePercentage();
                }

                // Fallback for other platforms - use process-based estimation
                return GetCrossplatformCpuUsagePercentage();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to calculate CPU usage");
                return 0;
            }
        }

        private double GetLinuxCpuUsagePercentage()
        {
            try
            {
                // Read CPU usage from /proc/stat
                const string cpuStatPath = "/proc/stat";
                var statLines = File.ReadAllLines(cpuStatPath);
                var cpuLine = statLines[0]; // First line is overall CPU

                // Format: cpu user nice system idle iowait irq softirq steal guest guest_nice
                var values = cpuLine.Split(SplitSeparators, StringSplitOptions.RemoveEmptyEntries);
                if (values.Length >= 5)
                {
                    var user = long.Parse(values[1], CultureInfo.InvariantCulture);
                    var nice = long.Parse(values[2], CultureInfo.InvariantCulture);
                    var system = long.Parse(values[3], CultureInfo.InvariantCulture);
                    var idle = long.Parse(values[4], CultureInfo.InvariantCulture);
                    var iowait = values.Length > 5 ? long.Parse(values[5], CultureInfo.InvariantCulture) : 0;

                    var totalIdle = idle + iowait;
                    var totalNonIdle = user + nice + system;
                    var total = totalIdle + totalNonIdle;

                    if (total > 0)
                    {
                        return Math.Max(0.0, Math.Min(100.0, (double)totalNonIdle / total * 100));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to read Linux CPU usage from /proc/stat");
            }

            return 0;
        }

        private double GetCrossplatformCpuUsagePercentage()
        {
            lock (_cpuLock)
            {
                try
                {
                    var currentTime = DateTime.UtcNow;
                    var currentTotalProcessorTime = _currentProcess.TotalProcessorTime;

                    var timeDiff = currentTime - _lastCpuTime;
                    var cpuTimeDiff = currentTotalProcessorTime - _lastTotalProcessorTime;

                    if (timeDiff.TotalMilliseconds > 500) // Only calculate if enough time has passed
                    {
                        // Calculate CPU usage: (CPU time used / (elapsed time * number of cores)) * 100
                        var cpuUsage = (cpuTimeDiff.TotalMilliseconds / (timeDiff.TotalMilliseconds * Environment.ProcessorCount)) * 100;

                        // Update for next calculation
                        _lastCpuTime = currentTime;
                        _lastTotalProcessorTime = currentTotalProcessorTime;

                        // This is process CPU usage, not system-wide, but better than nothing
                        return Math.Max(0.0, Math.Min(100.0, cpuUsage));
                    }

                    return 0; // Return 0 if not enough time has passed
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to calculate cross-platform CPU usage");
                    return 0;
                }
            }
        }

        private long GetLinuxAvailableMemoryMB()
        {
            try
            {
                // Read from /proc/meminfo
                const string memInfoPath = "/proc/meminfo";
                var lines = File.ReadAllLines(memInfoPath);
                long memAvailable = 0;
                long memFree = 0;
                long buffers = 0;
                long cached = 0;

                var splitOptions = StringSplitOptions.RemoveEmptyEntries;

                foreach (var line in lines)
                {
                    if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
                    {
                        var parts = line.Split(SplitSeparators, splitOptions);
                        if (parts.Length >= 2 && long.TryParse(parts[1], out memAvailable))
                        {
                            return memAvailable / 1024; // Convert KB to MB
                        }
                    }
                    else if (line.StartsWith("MemFree:", StringComparison.Ordinal))
                    {
                        var parts = line.Split(SplitSeparators, splitOptions);
                        if (parts.Length >= 2)
                        {
                            _ = long.TryParse(parts[1], out memFree);
                        }
                    }
                    else if (line.StartsWith("Buffers:", StringComparison.Ordinal))
                    {
                        var parts = line.Split(SplitSeparators, splitOptions);
                        if (parts.Length >= 2)
                        {
                            _ = long.TryParse(parts[1], out buffers);
                        }
                    }
                    else if (line.StartsWith("Cached:", StringComparison.Ordinal))
                    {
                        var parts = line.Split(SplitSeparators, splitOptions);
                        if (parts.Length >= 2)
                        {
                            _ = long.TryParse(parts[1], out cached);
                        }
                    }
                }

                // If MemAvailable is not available, estimate as MemFree + Buffers + Cached
                if (memAvailable == 0)
                {
                    memAvailable = memFree + buffers + cached;
                }

                return memAvailable / 1024; // Convert KB to MB
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to read Linux memory info from /proc/meminfo");
                return 0;
            }
        }

        private double GetLinuxMemoryUsagePercentage()
        {
            try
            {
                const string memInfoPath = "/proc/meminfo";
                var lines = File.ReadAllLines(memInfoPath);
                long memTotal = 0;
                long memAvailable = 0;

                var splitOptions = StringSplitOptions.RemoveEmptyEntries;

                foreach (var line in lines)
                {
                    if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
                    {
                        var parts = line.Split(SplitSeparators, splitOptions);
                        if (parts.Length >= 2)
                        {
                            _ = long.TryParse(parts[1], out memTotal);
                        }
                    }
                    else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
                    {
                        var parts = line.Split(SplitSeparators, splitOptions);
                        if (parts.Length >= 2)
                        {
                            _ = long.TryParse(parts[1], out memAvailable);
                        }
                    }
                }

                if (memTotal > 0 && memAvailable >= 0)
                {
                    var memUsed = memTotal - memAvailable;
                    return Math.Max(0.0, Math.Min(100.0, (double)memUsed / memTotal * 100));
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to calculate Linux memory usage percentage");
            }

            return 0;
        }

        private long GetTotalPhysicalMemoryMB()
        {
            try
            {
                if (_isWindows)
                {
                    // Use Windows API to get total physical memory
                    var memStatus = new MEMORYSTATUSEX();
                    if (GlobalMemoryStatusEx(memStatus))
                    {
                        return (long)(memStatus.UllTotalPhys / (1024 * 1024));
                    }
                }
                else if (_isLinux)
                {
                    // Read from /proc/meminfo
                    const string memInfoPath = "/proc/meminfo";
                    var lines = File.ReadAllLines(memInfoPath);
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
                        {
                            var parts = line.Split(SplitSeparators, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2 && long.TryParse(parts[1], out var memTotal))
                            {
                                return memTotal / 1024; // Convert KB to MB
                            }
                        }
                    }
                }

                // Fallback estimation
                return Math.Max(4096, GC.GetTotalMemory(false) / (1024 * 1024) * 8); // Rough estimate
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to get total physical memory");
                return 8192; // 8GB fallback
            }
        }

        private long GetFallbackAvailableMemoryMB()
        {
            // Conservative fallback estimation
            var processMemoryMB = _currentProcess.WorkingSet64 / (1024 * 1024);
            var totalEstimateMB = Math.Max(4096, processMemoryMB * 8); // Assume process uses ~12.5% of total
            return Math.Max(1024, totalEstimateMB - processMemoryMB);
        }

        private double GetFallbackMemoryUsagePercentage()
        {
            // Conservative fallback - assume moderate usage
            var processMemoryMB = _currentProcess.WorkingSet64 / (1024 * 1024);
            var estimatedTotalMB = Math.Max(4096, processMemoryMB * 8);
            return Math.Min(100.0, (double)processMemoryMB / estimatedTotalMB * 100);
        }

        // Windows API for memory information
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            private uint dwLength;
            private uint dwMemoryLoad;
            private ulong ullTotalPhys;
            private ulong ullAvailPhys;
            private ulong ullTotalPageFile;
            private ulong ullAvailPageFile;
            private ulong ullTotalVirtual;
            private ulong ullAvailVirtual;
            private ulong ullAvailExtendedVirtual;

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
            }

            public ulong UllTotalPhys => ullTotalPhys;
        }
    }
}
