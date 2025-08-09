// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Threading
{
    /// <summary>
    /// Service interface for throttling resource usage.
    /// </summary>
    public interface IResourceThrottlingService
    {
        /// <summary>
        /// Acquires a throttling semaphore for the specified operation type.
        /// </summary>
        /// <param name="operationType">The type of operation.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A disposable that releases the semaphore when disposed.</returns>
        Task<IDisposable> AcquireAsync(
            OperationType operationType,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Tries to acquire a throttling semaphore for the specified operation type without waiting.
        /// </summary>
        /// <param name="operationType">The type of operation.</param>
        /// <param name="semaphoreRelease">The disposable that releases the semaphore when disposed.</param>
        /// <returns>True if the semaphore was acquired; otherwise, false.</returns>
        bool TryAcquire(
            OperationType operationType,
            out IDisposable semaphoreRelease);
    }
}
