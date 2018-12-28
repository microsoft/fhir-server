// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.CosmosDb.Features.Storage
{
    public interface ICosmosDbDistributedLock : IDisposable
    {
        /// <summary>
        /// Makes one attempt to acquire the lock.
        /// </summary>
        /// <returns>A task that completes when the attempt completes. The boolean task argument indicates whether the lock was successfully acquired</returns>
        Task<bool> TryAcquireLock();

        /// <summary>
        /// Acquires the lock.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token</param>
        /// <returns>A task that completes when the lock has been acquired.</returns>
        Task AcquireLock(CancellationToken cancellationToken);

        /// <summary>
        /// Releases the lock.
        /// </summary>
        /// <returns>A task that completes when the lock has been released</returns>
        Task ReleaseLock();
    }
}
