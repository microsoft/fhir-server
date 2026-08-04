// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Health.Fhir.Core.Features.Tenancy
{
    /// <summary>
    /// Default <see cref="ITenantContainer"/> implementation.
    /// </summary>
    /// <remarks>
    /// Disposal drains outstanding leases before the underlying service provider is torn down so in-flight
    /// work never observes a disposed tenant container.
    /// </remarks>
    public sealed class TenantContainer : ITenantContainer
    {
        private const int StateOpen = 0;
        private const int StateDraining = 1;

        private readonly TenantDescriptor _tenant;
        private readonly ServiceProvider _provider;
        private readonly TimeProvider _timeProvider;
        private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<IHostedService> _startedInitializers = new();

        private int _refCount;
        private int _state = StateOpen;
        private long _lastAccessedTicks;

        /// <summary>
        /// Initializes a new instance of the <see cref="TenantContainer"/> class.
        /// </summary>
        /// <param name="tenant">The tenant this container serves.</param>
        /// <param name="provider">The tenant's service provider. This container takes ownership of it.</param>
        /// <param name="timeProvider">The time provider used for idle tracking.</param>
        public TenantContainer(TenantDescriptor tenant, ServiceProvider provider, TimeProvider timeProvider)
        {
            EnsureArg.IsNotNull(tenant, nameof(tenant));
            EnsureArg.IsNotNull(provider, nameof(provider));
            EnsureArg.IsNotNull(timeProvider, nameof(timeProvider));

            _tenant = tenant;
            _provider = provider;
            _timeProvider = timeProvider;
            _lastAccessedTicks = timeProvider.GetUtcNow().UtcTicks;
        }

        /// <inheritdoc />
        public TenantId TenantId => _tenant.TenantId;

        /// <inheritdoc />
        public int ActiveLeaseCount => Volatile.Read(ref _refCount);

        /// <inheritdoc />
        public DateTimeOffset LastAccessedUtc => new(Interlocked.Read(ref _lastAccessedTicks), TimeSpan.Zero);

        /// <inheritdoc />
        public bool TryAcquire([NotNullWhen(true)] out ITenantLease lease)
        {
            Interlocked.Increment(ref _refCount);

            if (Volatile.Read(ref _state) != StateOpen)
            {
                ReleaseCore();
                lease = null;
                return false;
            }

            Interlocked.Exchange(ref _lastAccessedTicks, _timeProvider.GetUtcNow().UtcTicks);

            lease = new TenantLease(this);
            return true;
        }

        /// <inheritdoc />
        public async Task StartInitializersAsync(CancellationToken cancellationToken)
        {
            foreach (IHostedService initializer in _provider.GetServices<IHostedService>())
            {
                await initializer.StartAsync(cancellationToken).ConfigureAwait(false);
                _startedInitializers.Add(initializer);
            }
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _state, StateDraining) == StateDraining)
            {
                return;
            }

            TryCompleteDrain();

            await _drained.Task.ConfigureAwait(false);

            try
            {
                for (int i = _startedInitializers.Count - 1; i >= 0; i--)
                {
                    await _startedInitializers[i].StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                _startedInitializers.Clear();
                await _provider.DisposeAsync().ConfigureAwait(false);
            }
        }

        private void ReleaseCore()
        {
            Interlocked.Decrement(ref _refCount);

            TryCompleteDrain();
        }

        private void TryCompleteDrain()
        {
            if (Volatile.Read(ref _state) == StateDraining && Volatile.Read(ref _refCount) == 0)
            {
                _drained.TrySetResult();
            }
        }

        private sealed class TenantLease : ITenantLease
        {
            private readonly TenantContainer _container;
            private int _disposed;

            public TenantLease(TenantContainer container)
            {
                _container = container;
            }

            public TenantId TenantId => _container.TenantId;

            public IServiceProvider Services => _container._provider;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    _container.ReleaseCore();
                }
            }
        }
    }
}
