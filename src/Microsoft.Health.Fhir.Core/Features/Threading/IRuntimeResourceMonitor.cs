// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Threading
{
    /// <summary>
    /// Interface for monitoring runtime system resources.
    /// </summary>
    public interface IRuntimeResourceMonitor : System.IDisposable
    {
        /// <summary>
        /// Gets the current processor count (may change in containerized environments).
        /// </summary>
        int GetCurrentProcessorCount();

        /// <summary>
        /// Gets the current available memory in MB.
        /// </summary>
        long GetCurrentAvailableMemoryMB();

        /// <summary>
        /// Gets the current memory usage percentage.
        /// </summary>
        double GetCurrentMemoryUsagePercentage();

        /// <summary>
        /// Checks if the system is currently under resource pressure.
        /// </summary>
        bool IsUnderResourcePressure();
    }
}
