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
        private const int DrainingMask = int.MinValue;
        private const int LeaseCountMask = int.MaxValue;

        private readonly TenantDescriptor _tenant;
        private readonly ServiceProvider _provider;
        private readonly TimeProvider _timeProvider;
        private readonly ITenantContextAccessor _tenantContextAccessor;
        private readonly object _lifecycleSync = new();
        private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<IHostedService> _startedInitializers = new();

        private Task _disposeTask;
        private int _leaseState;
        private Task _startInitializersTask;
        private long _lastAccessedTicks;

        /// <summary>
        /// Initializes a new instance of the <see cref="TenantContainer"/> class.
        /// </summary>
        /// <param name="tenant">The tenant this container serves.</param>
        /// <param name="provider">The tenant's service provider. This container takes ownership of it.</param>
        /// <param name="timeProvider">The time provider used for idle tracking.</param>
        public TenantContainer(TenantDescriptor tenant, ServiceProvider provider, TimeProvider timeProvider)
            : this(tenant, provider, timeProvider, provider?.GetService<ITenantContextAccessor>())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TenantContainer"/> class.
        /// </summary>
        /// <param name="tenant">The tenant this container serves.</param>
        /// <param name="provider">The tenant's service provider. This container takes ownership of it.</param>
        /// <param name="timeProvider">The time provider used for idle tracking.</param>
        /// <param name="tenantContextAccessor">
        /// The ambient tenant context accessor. When provided, <see cref="IHostedService"/> initializers
        /// start and stop under this tenant's ambient context and restore the prior value on all outcomes.
        /// </param>
        public TenantContainer(TenantDescriptor tenant, ServiceProvider provider, TimeProvider timeProvider, ITenantContextAccessor tenantContextAccessor)
        {
            EnsureArg.IsNotNull(tenant, nameof(tenant));
            EnsureArg.IsNotNull(provider, nameof(provider));
            EnsureArg.IsNotNull(timeProvider, nameof(timeProvider));

            _tenant = tenant;
            _provider = provider;
            _timeProvider = timeProvider;
            _tenantContextAccessor = tenantContextAccessor;
            _lastAccessedTicks = timeProvider.GetUtcNow().UtcTicks;
        }

        /// <inheritdoc />
        public TenantId TenantId => _tenant.TenantId;

        /// <inheritdoc />
        public int ActiveLeaseCount => GetLeaseCount(Volatile.Read(ref _leaseState));

        /// <inheritdoc />
        public DateTimeOffset LastAccessedUtc => new(Interlocked.Read(ref _lastAccessedTicks), TimeSpan.Zero);

        private bool IsDraining => IsDrainingState(Volatile.Read(ref _leaseState));

        /// <inheritdoc />
        public bool TryAcquire([NotNullWhen(true)] out ITenantLease lease)
        {
            while (true)
            {
                int state = Volatile.Read(ref _leaseState);

                if (IsDrainingState(state))
                {
                    lease = null;
                    return false;
                }

                if (GetLeaseCount(state) == LeaseCountMask)
                {
                    throw new InvalidOperationException("Tenant container lease count exceeded the supported maximum.");
                }

                if (Interlocked.CompareExchange(ref _leaseState, state + 1, state) == state)
                {
                    Interlocked.Exchange(ref _lastAccessedTicks, _timeProvider.GetUtcNow().UtcTicks);
                    lease = new TenantLease(this);
                    return true;
                }
            }
        }

        /// <inheritdoc />
        public bool TryBeginDrainIfIdle(DateTimeOffset? expectedLastAccessedUtc = null)
        {
            lock (_lifecycleSync)
            {
                long? expectedLastAccessedTicks = expectedLastAccessedUtc?.UtcTicks;

                if (!MatchesExpectedLastAccessed(expectedLastAccessedTicks))
                {
                    return false;
                }

                if (Interlocked.CompareExchange(ref _leaseState, DrainingMask, 0) != 0)
                {
                    return false;
                }

                if (!MatchesExpectedLastAccessed(expectedLastAccessedTicks))
                {
                    RollBackIdleDrainClaim();
                    return false;
                }

                CompleteDrainIfIdle(DrainingMask);
                return true;
            }
        }

        /// <inheritdoc />
        public Task StartInitializersAsync(CancellationToken cancellationToken)
        {
            lock (_lifecycleSync)
            {
                if (IsDraining)
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
                    await ExecuteInTenantContextAsync(
                        () => initializer.StartAsync(cancellationToken)).ConfigureAwait(false);

                    lock (_lifecycleSync)
                    {
                        _startedInitializers.Add(initializer);
                    }
                }
            }
            catch
            {
                BeginDrain();
                throw;
            }
        }

        private async Task DisposeCoreAsync()
        {
            BeginDrain();

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
                    await ExecuteInTenantContextAsync(
                        () => startedInitializers[i].StopAsync(CancellationToken.None)).ConfigureAwait(false);
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

        private async Task ExecuteInTenantContextAsync(Func<Task> operation)
        {
            TenantId priorTenant = _tenantContextAccessor?.Current ?? default;
            _tenantContextAccessor?.SetCurrent(_tenant.TenantId);
            try
            {
                await operation().ConfigureAwait(false);
            }
            finally
            {
                _tenantContextAccessor?.SetCurrent(priorTenant);
            }
        }

        private void ReleaseCore()
        {
            while (true)
            {
                int state = Volatile.Read(ref _leaseState);
                int leaseCount = GetLeaseCount(state);

                if (leaseCount == 0)
                {
                    throw new InvalidOperationException("Tenant container lease count cannot be negative.");
                }

                int nextState = state - 1;

                if (Interlocked.CompareExchange(ref _leaseState, nextState, state) == state)
                {
                    CompleteDrainIfIdle(nextState);
                    return;
                }
            }
        }

        private void BeginDrain()
        {
            lock (_lifecycleSync)
            {
                while (true)
                {
                    int state = Volatile.Read(ref _leaseState);

                    if (IsDrainingState(state))
                    {
                        CompleteDrainIfIdle(state);
                        return;
                    }

                    int drainingState = state | DrainingMask;

                    if (Interlocked.CompareExchange(ref _leaseState, drainingState, state) == state)
                    {
                        CompleteDrainIfIdle(drainingState);
                        return;
                    }
                }
            }
        }

        private void CompleteDrainIfIdle(int leaseState)
        {
            if (leaseState == DrainingMask)
            {
                _drained.TrySetResult();
            }
        }

        private bool MatchesExpectedLastAccessed(long? expectedLastAccessedTicks) =>
            !expectedLastAccessedTicks.HasValue ||
            Interlocked.Read(ref _lastAccessedTicks) == expectedLastAccessedTicks.Value;

        private void RollBackIdleDrainClaim()
        {
            if (Interlocked.CompareExchange(ref _leaseState, 0, DrainingMask) != DrainingMask)
            {
                throw new InvalidOperationException("Tenant container idle-drain claim could not be rolled back.");
            }
        }

        private static int GetLeaseCount(int leaseState) => leaseState & LeaseCountMask;

        private static bool IsDrainingState(int leaseState) => (leaseState & DrainingMask) != 0;

        private static void AddFailure(List<ExceptionDispatchInfo> failures, Exception exception)
        {
            if (exception is AggregateException aggregateException &&
                aggregateException.InnerExceptions.Count > 0)
            {
                foreach (Exception innerException in aggregateException.InnerExceptions)
                {
                    AddFailure(failures, innerException);
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
