// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
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
        private readonly object _lifecycleSync = new();
        private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<IHostedService> _startedInitializers = new();

        private Task _disposeTask;
        private int _refCount;
        private Task _startInitializersTask;
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
        public Task StartInitializersAsync(CancellationToken cancellationToken)
        {
            lock (_lifecycleSync)
            {
                if (Volatile.Read(ref _state) != StateOpen)
                {
                    throw new InvalidOperationException("Tenant container initializers cannot be started after disposal begins.");
                }

                _startInitializersTask ??= StartInitializersCoreAsync(cancellationToken);
                return _startInitializersTask;
            }
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            Task disposeTask = Volatile.Read(ref _disposeTask);

            if (disposeTask is not null)
            {
                return new ValueTask(disposeTask);
            }

            lock (_lifecycleSync)
            {
                _disposeTask ??= DisposeCoreAsync();
                return new ValueTask(_disposeTask);
            }
        }

        private async Task StartInitializersCoreAsync(CancellationToken cancellationToken)
        {
            try
            {
                foreach (IHostedService initializer in _provider.GetServices<IHostedService>())
                {
                    await initializer.StartAsync(cancellationToken).ConfigureAwait(false);

                    lock (_lifecycleSync)
                    {
                        _startedInitializers.Add(initializer);
                    }
                }
            }
            catch
            {
                Interlocked.CompareExchange(ref _state, StateDraining, StateOpen);
                TryCompleteDrain();
                throw;
            }
        }

        private async Task DisposeCoreAsync()
        {
            Interlocked.Exchange(ref _state, StateDraining);

            Task startInitializersTask;

            lock (_lifecycleSync)
            {
                startInitializersTask = _startInitializersTask;
            }

            List<ExceptionDispatchInfo> failures = [];

            if (startInitializersTask is not null)
            {
                try
                {
                    await startInitializersTask.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    AddFailure(failures, ex);
                }
            }

            TryCompleteDrain();

            await _drained.Task.ConfigureAwait(false);

            List<IHostedService> startedInitializers;

            lock (_lifecycleSync)
            {
                startedInitializers = [.. _startedInitializers];
                _startedInitializers.Clear();
            }

            for (int i = startedInitializers.Count - 1; i >= 0; i--)
            {
                try
                {
                    await startedInitializers[i].StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    AddFailure(failures, ex);
                }
            }

            try
            {
                await _provider.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AddFailure(failures, ex);
            }

            ThrowIfAnyFailures(failures);
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

        private static void AddFailure(List<ExceptionDispatchInfo> failures, Exception exception)
        {
            if (exception is AggregateException aggregateException)
            {
                foreach (Exception innerException in aggregateException.InnerExceptions)
                {
                    failures.Add(ExceptionDispatchInfo.Capture(innerException));
                }

                return;
            }

            failures.Add(ExceptionDispatchInfo.Capture(exception));
        }

        private static void ThrowIfAnyFailures(List<ExceptionDispatchInfo> failures)
        {
            if (failures.Count == 0)
            {
                return;
            }

            if (failures.Count == 1)
            {
                failures[0].Throw();
            }

            List<Exception> exceptions = new(failures.Count);

            foreach (ExceptionDispatchInfo failure in failures)
            {
                exceptions.Add(failure.SourceException);
            }

            throw new AggregateException(exceptions);
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
