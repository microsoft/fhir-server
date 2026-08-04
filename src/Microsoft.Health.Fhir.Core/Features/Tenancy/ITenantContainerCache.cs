// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Tenancy
{
    /// <summary>
    /// Holds the resident set of tenant containers, creating them on demand and evicting them when idle.
    /// </summary>
    /// <remarks>
    /// Implementations are expected to stop admitting new work once asynchronous disposal begins, wait for
    /// outstanding leases to be released, and then dispose the resident tenant containers.
    /// </remarks>
    public interface ITenantContainerCache : IAsyncDisposable
    {
        /// <summary>
        /// Gets the number of resident tenant containers.
        /// </summary>
        /// <remarks>
        /// The value is a point-in-time snapshot when cache operations are running concurrently.
        /// </remarks>
        int Count { get; }

        /// <summary>
        /// Acquires a lease on the tenant's container, creating the container if necessary.
        /// </summary>
        /// <param name="tenant">The tenant to acquire.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A lease the caller must dispose when the unit of work completes.</returns>
        /// <exception cref="TenantAdmissionRejectedException">
        /// The resident cap is reached and no container is evictable.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        /// The cancellation token was canceled before the acquisition completed.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// Cache disposal has begun, so no new acquisition can be accepted.
        /// </exception>
        ValueTask<ITenantLease> AcquireAsync(TenantDescriptor tenant, CancellationToken cancellationToken);

        /// <summary>
        /// Evicts every container that has been idle for longer than the configured timeout.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        /// <exception cref="OperationCanceledException">
        /// The cancellation token was canceled before eviction completed.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// Cache disposal has begun, so no new sweep can be accepted.
        /// </exception>
        ValueTask EvictIdleAsync(CancellationToken cancellationToken);
    }
}
