// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Threading
{
    /// <summary>
    /// Service interface for dynamic threading configurations.
    /// </summary>
    public interface IDynamicThreadingService
    {
        /// <summary>
        /// Gets the optimal thread count for the specified operation type.
        /// </summary>
        /// <param name="operationType">The type of operation.</param>
        /// <returns>The optimal number of threads.</returns>
        int GetOptimalThreadCount(OperationType operationType);

        /// <summary>
        /// Gets the maximum concurrent operations for the specified type.
        /// </summary>
        /// <param name="operationType">The type of operation.</param>
        /// <returns>The maximum number of concurrent operations.</returns>
        int GetMaxConcurrentOperations(OperationType operationType);

        /// <summary>
        /// Checks if the system has sufficient resources for the operation.
        /// </summary>
        /// <param name="operationType">The type of operation.</param>
        /// <param name="requestedThreads">The number of threads requested.</param>
        /// <returns>True if sufficient resources are available.</returns>
        bool HasSufficientResources(
            OperationType operationType,
            int requestedThreads);
    }
}
