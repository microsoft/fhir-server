// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Tenancy
{
    /// <summary>
    /// Owns the dependency injection container for a single tenant, together with the reference count
    /// that keeps it alive while work is in flight.
    /// </summary>
    public interface ITenantContainer : IAsyncDisposable
    {
        /// <summary>
        /// Gets the tenant this container serves.
        /// </summary>
        TenantId TenantId { get; }

        /// <summary>
        /// Gets the number of outstanding leases.
        /// </summary>
        int ActiveLeaseCount { get; }

        /// <summary>
        /// Gets the time at which a lease was most recently acquired.
        /// </summary>
        DateTimeOffset LastAccessedUtc { get; }

        /// <summary>
        /// Attempts to take a lease on this container.
        /// </summary>
        /// <param name="lease">
        /// When this method returns <see langword="true"/>, contains the acquired lease. Otherwise,
        /// <see langword="null"/>.
        /// </param>
        /// <returns><see langword="false"/> if the container is draining and can no longer accept work.</returns>
        bool TryAcquire([NotNullWhen(true)] out ITenantLease lease);

        /// <summary>
        /// Atomically begins draining this container only when it is open and has no outstanding leases.
        /// </summary>
        /// <param name="expectedLastAccessedUtc">
        /// When supplied, the last-access snapshot that must still be current for the claim to succeed.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when this call changed an idle container to draining; otherwise,
        /// <see langword="false"/> without changing the container state.
        /// </returns>
        /// <remarks>
        /// A successful claim prevents all later lease acquisitions. This method linearizes with
        /// <see cref="TryAcquire(out ITenantLease)"/> so a lease and an idle-drain claim cannot both win.
        /// The caller remains responsible for completing teardown through
        /// <see cref="IAsyncDisposable.DisposeAsync"/>.
        /// </remarks>
        bool TryBeginDrainIfIdle(DateTimeOffset? expectedLastAccessedUtc = null);

        /// <summary>
        /// Starts the hosted services that were classified as per-tenant initializers.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when startup is requested after the container has begun draining.
        /// </exception>
        Task StartInitializersAsync(CancellationToken cancellationToken);
    }
}
