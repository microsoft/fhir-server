// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Health.Fhir.Core.Features.Tenancy;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Tenancy
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class TenantEvictionSafetyTests
    {
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);
        private readonly ITestOutputHelper _output;

        public TenantEvictionSafetyTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task GivenAnInFlightLease_WhenCacheDisposalBegins_ThenTheScopedServiceRemainsUsableUntilTheLeaseReleases()
        {
            var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero));
            var factory = new CoordinatedFactory(timeProvider);
            TenantContainerCache cache = CreateCache(factory, timeProvider);
            ITenantLease lease = null;
            IServiceScope scope = null;
            ProviderDisposalGate disposalGate = null;
            bool bodySucceeded = false;

            try
            {
                using var cancellationTokenSource = new CancellationTokenSource(TestTimeout);
                lease = await AwaitWithinAsync(
                    cache.AcquireAsync(Tenant("alpha"), cancellationTokenSource.Token).AsTask(),
                    cancellationTokenSource.Token);
                scope = lease.Services.CreateScope();
                var work = scope.ServiceProvider.GetRequiredService<ScopedWork>();

                // Resolve the gate after the provider-owned disposable so DI's LIFO teardown enters the
                // gate before the owned resource is actually cleaned up.
                ProviderOwnedDisposable providerOwnedDisposable =
                    lease.Services.GetRequiredService<ProviderOwnedDisposable>();
                disposalGate = lease.Services.GetRequiredService<ProviderDisposalGate>();
                CoordinatedContainer container = factory.Container;

                Task cacheDisposal = cache.DisposeAsync().AsTask();

                await AwaitWithinAsync(container.DrainStarted.Task, cancellationTokenSource.Token);

                Assert.False(cacheDisposal.IsCompleted);
                Assert.False(disposalGate.DisposalEntered.Task.IsCompleted);
                Assert.False(providerOwnedDisposable.IsDisposed);
                Assert.False(work.IsDisposed);

                work.Touch();
                Assert.Same(work, scope.ServiceProvider.GetRequiredService<ScopedWork>());

                lease.Dispose();
                lease = null;

                await AwaitWithinAsync(disposalGate.DisposalEntered.Task, cancellationTokenSource.Token);

                Assert.False(cacheDisposal.IsCompleted);
                Assert.False(providerOwnedDisposable.IsDisposed);
                Assert.Equal(1, container.DisposeCallCount);

                disposalGate.AllowDisposal();
                await AwaitWithinAsync(cacheDisposal, cancellationTokenSource.Token);

                Assert.Equal(1, disposalGate.DisposeCallCount);
                Assert.Equal(1, providerOwnedDisposable.DisposeCallCount);
                Assert.Equal(1, container.DisposeInvocationCount);
                Assert.Equal(1, container.DisposeCallCount);

                scope.Dispose();
                scope = null;

                Assert.Equal(1, work.DisposeCallCount);
                bodySucceeded = true;
            }
            finally
            {
                DisposeWithDiagnostics(scope, "scope", bodySucceeded);
                DisposeWithDiagnostics(lease, "lease", bodySucceeded);
                disposalGate?.AllowDisposal();
                await DisposeCacheAsync(cache, bodySucceeded);
            }
        }

        [Fact]
        public async Task GivenAnAcquireAtTheIdleDrainClaim_WhenSweeping_ThenTheProviderLivesUntilTheReturnedLeaseReleases()
        {
            // The frozen clock is load-bearing: the idle-drain claim must fail because of the lease-count
            // CAS, not because time advances during the race.
            var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero));
            var factory = new CoordinatedFactory(timeProvider);
            TenantContainerCache cache = CreateCache(factory, timeProvider, maxResidentTenants: 1, idleTimeout: TimeSpan.Zero);
            ITenantLease initialLease = null;
            ITenantLease racingLease = null;
            ProviderDisposalGate disposalGate = null;
            using var allowDrainClaim = new ManualResetEventSlim();
            bool bodySucceeded = false;

            try
            {
                using var cancellationTokenSource = new CancellationTokenSource(TestTimeout);
                initialLease = await AwaitWithinAsync(
                    cache.AcquireAsync(Tenant("alpha"), cancellationTokenSource.Token).AsTask(),
                    cancellationTokenSource.Token);
                ProviderOwnedDisposable providerOwnedDisposable =
                    initialLease.Services.GetRequiredService<ProviderOwnedDisposable>();
                disposalGate = initialLease.Services.GetRequiredService<ProviderDisposalGate>();
                CoordinatedContainer resident = factory.Container;
                var drainClaimEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                initialLease.Dispose();
                initialLease = null;
                resident.BeforeIdleDrainClaim = () =>
                {
                    try
                    {
                        Assert.Equal(0, resident.ActiveLeaseCount);
                        resident.BeforeIdleDrainClaim = null;
                        drainClaimEntered.TrySetResult(true);

                        if (!allowDrainClaim.Wait(TestTimeout))
                        {
                            throw new TimeoutException("The test did not release the idle-drain claim.");
                        }
                    }
                    catch (Exception exception)
                    {
                        drainClaimEntered.TrySetException(exception);
                        throw;
                    }
                };

                Task sweep = Task.Run(async () =>
                    await cache.EvictIdleAsync(cancellationTokenSource.Token));
                await AwaitWithinAsync(drainClaimEntered.Task, cancellationTokenSource.Token);
                racingLease = await AwaitWithinAsync(
                    cache.AcquireAsync(Tenant("alpha"), cancellationTokenSource.Token).AsTask(),
                    cancellationTokenSource.Token);
                allowDrainClaim.Set();
                await AwaitWithinAsync(sweep, cancellationTokenSource.Token);

                Assert.Equal(new TenantId("alpha"), racingLease.TenantId);
                Assert.Equal(1, resident.ActiveLeaseCount);
                Assert.Equal(1, cache.Count);
                Assert.Equal(0, cache.EvictionCount);
                Assert.Equal(0, resident.DisposeCallCount);
                Assert.False(providerOwnedDisposable.IsDisposed);

                racingLease.Dispose();
                racingLease = null;

                sweep = cache.EvictIdleAsync(cancellationTokenSource.Token).AsTask();
                await AwaitWithinAsync(disposalGate.DisposalEntered.Task, cancellationTokenSource.Token);

                Assert.False(providerOwnedDisposable.IsDisposed);
                Assert.Equal(1, resident.DisposeInvocationCount);
                Assert.Equal(1, resident.DisposeCallCount);

                disposalGate.AllowDisposal();
                await AwaitWithinAsync(sweep, cancellationTokenSource.Token);

                Assert.Equal(0, cache.Count);
                Assert.Equal(1, cache.EvictionCount);
                Assert.Equal(1, providerOwnedDisposable.DisposeCallCount);
                Assert.Equal(1, resident.DisposeInvocationCount);
                Assert.Equal(1, resident.DisposeCallCount);
                bodySucceeded = true;
            }
            finally
            {
                allowDrainClaim.Set();
                DisposeWithDiagnostics(racingLease, "racing lease", bodySucceeded);
                DisposeWithDiagnostics(initialLease, "initial lease", bodySucceeded);
                disposalGate?.AllowDisposal();
                await DisposeCacheAsync(cache, bodySucceeded);
            }
        }

        [Fact]
        public async Task GivenBoundedConcurrentTrafficAndAggressiveEviction_WhenLeasesAreHeld_ThenWorkNeverObservesProviderTeardown()
        {
            const int workerCount = 4;
            const int iterationsPerWorker = 40;
            const int tenantCount = 6;

            var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero));
            var factory = new CoordinatedFactory(timeProvider);
            await using TenantContainerCache cache =
                CreateCache(factory, timeProvider, maxResidentTenants: 1, idleTimeout: TimeSpan.Zero);
            var failures = new ConcurrentBag<Exception>();
            var successfulLeases = 0;
            var admissionRejections = 0;

            async Task WorkerAsync(int workerIndex, CancellationToken cancellationToken)
            {
                for (int iteration = 0; iteration < iterationsPerWorker; iteration++)
                {
                    ITenantLease lease = null;

                    try
                    {
                        lease = await cache.AcquireAsync(
                            Tenant($"tenant-{(workerIndex + iteration) % tenantCount}"),
                            cancellationToken);
                        using IServiceScope scope = lease.Services.CreateScope();
                        ScopedWork work = scope.ServiceProvider.GetRequiredService<ScopedWork>();

                        await Task.Yield();

                        work.Touch();
                        Interlocked.Increment(ref successfulLeases);
                    }
                    catch (TenantAdmissionRejectedException)
                    {
                        Interlocked.Increment(ref admissionRejections);
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception);
                    }
                    finally
                    {
                        lease?.Dispose();
                    }
                }
            }

            using var cancellationTokenSource = new CancellationTokenSource(TestTimeout);
            Task[] workers = Enumerable.Range(0, workerCount)
                .Select(workerIndex => WorkerAsync(workerIndex, cancellationTokenSource.Token))
                .ToArray();

            await AwaitWithinAsync(Task.WhenAll(workers), cancellationTokenSource.Token);

            _output.WriteLine(
                $"Stress attempts={workerCount * iterationsPerWorker}; successes={successfulLeases}; admission rejections={admissionRejections}.");
            Assert.Empty(failures.Select(exception => $"{exception.GetType().Name}: {exception.Message}"));
            Assert.Equal(workerCount * iterationsPerWorker, successfulLeases + admissionRejections);
        }

        private static async Task<T> AwaitWithinAsync<T>(Task<T> task, CancellationToken cancellationToken) =>
            await task.WaitAsync(TestTimeout, cancellationToken);

        private static async Task AwaitWithinAsync(Task task, CancellationToken cancellationToken) =>
            await task.WaitAsync(TestTimeout, cancellationToken);

        private async Task DisposeCacheAsync(TenantContainerCache cache, bool bodySucceeded)
        {
            try
            {
                await cache.DisposeAsync().AsTask().WaitAsync(TestTimeout);
            }
            catch (Exception exception) when (!bodySucceeded)
            {
                _output.WriteLine($"cache cleanup failed after body failure: {exception.GetType().Name}: {exception.Message}");
            }
        }

        private void DisposeWithDiagnostics(IDisposable disposable, string resourceName, bool bodySucceeded)
        {
            if (disposable is null)
            {
                return;
            }

            try
            {
                disposable.Dispose();
            }
            catch (Exception exception) when (!bodySucceeded)
            {
                _output.WriteLine($"{resourceName} cleanup failed after body failure: {exception.GetType().Name}: {exception.Message}");
            }
        }

        private static TenantContainerCache CreateCache(
            ITenantContainerFactory factory,
            TimeProvider timeProvider,
            int maxResidentTenants = 50,
            TimeSpan? idleTimeout = null) =>
            new(
                factory,
                Options.Create(new TenantContainerCacheOptions
                {
                    MaxResidentTenants = maxResidentTenants,
                    IdleTimeout = idleTimeout ?? TimeSpan.FromMinutes(30),
                }),
                timeProvider);

        private static TenantDescriptor Tenant(string id) => new(new TenantId(id));

        private sealed class CoordinatedFactory : ITenantContainerFactory
        {
            private readonly TimeProvider _timeProvider;

            public CoordinatedFactory(TimeProvider timeProvider)
            {
                _timeProvider = timeProvider;
            }

            public CoordinatedContainer Container { get; private set; }

            public ValueTask<ITenantContainer> CreateAsync(
                TenantDescriptor tenant,
                CancellationToken cancellationToken)
            {
                var services = new ServiceCollection();
                services.AddSingleton<ProviderOwnedDisposable>();
                services.AddSingleton<ProviderDisposalGate>();
                services.AddScoped<ScopedWork>();

                ServiceProvider provider = services.BuildServiceProvider(
                    new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false });
                Container = new CoordinatedContainer(tenant, provider, _timeProvider);

                return ValueTask.FromResult<ITenantContainer>(Container);
            }
        }

        private sealed class CoordinatedContainer : ITenantContainer
        {
            private readonly TenantContainer _inner;
            private readonly object _disposeSync = new();
            private Task _disposeTask;
            private int _disposeInvocationCount;
            private int _disposeCallCount;

            public CoordinatedContainer(TenantDescriptor tenant, ServiceProvider provider, TimeProvider timeProvider)
            {
                _inner = new TenantContainer(tenant, provider, timeProvider);
            }

            public int ActiveLeaseCount => _inner.ActiveLeaseCount;

            public Action BeforeIdleDrainClaim { get; set; }

            public TaskCompletionSource<bool> DrainStarted { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public int DisposeInvocationCount => Volatile.Read(ref _disposeInvocationCount);

            public int DisposeCallCount => Volatile.Read(ref _disposeCallCount);

            public DateTimeOffset LastAccessedUtc => _inner.LastAccessedUtc;

            public TenantId TenantId => _inner.TenantId;

            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref _disposeInvocationCount);
                lock (_disposeSync)
                {
                    _disposeTask ??= DisposeCoreAsync();
                    return new ValueTask(_disposeTask);
                }
            }

            public Task StartInitializersAsync(CancellationToken cancellationToken) =>
                _inner.StartInitializersAsync(cancellationToken);

            public bool TryAcquire(out ITenantLease lease) => _inner.TryAcquire(out lease);

            public bool TryBeginDrainIfIdle(DateTimeOffset? expectedLastAccessedUtc = null)
            {
                BeforeIdleDrainClaim?.Invoke();
                return _inner.TryBeginDrainIfIdle(expectedLastAccessedUtc);
            }

            private async Task DisposeCoreAsync()
            {
                Interlocked.Increment(ref _disposeCallCount);
                Task disposal = _inner.DisposeAsync().AsTask();
                DrainStarted.TrySetResult(true);
                await disposal;
            }
        }

        private sealed class ProviderDisposalGate : IAsyncDisposable
        {
            private int _disposeCallCount;

            public TaskCompletionSource<bool> DisposalMayComplete { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> DisposalEntered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public int DisposeCallCount => Volatile.Read(ref _disposeCallCount);

            public async ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref _disposeCallCount);
                DisposalEntered.TrySetResult(true);
                await DisposalMayComplete.Task;
            }

            public void AllowDisposal() => DisposalMayComplete.TrySetResult(true);
        }

        private sealed class ProviderOwnedDisposable : IDisposable
        {
            private int _disposeCallCount;

            public int DisposeCallCount => Volatile.Read(ref _disposeCallCount);

            public bool IsDisposed => DisposeCallCount > 0;

            public void Dispose() => Interlocked.Increment(ref _disposeCallCount);
        }

        private sealed class ScopedWork : IDisposable
        {
            private int _disposeCallCount;
            private int _touchCount;

            public int DisposeCallCount => Volatile.Read(ref _disposeCallCount);

            public bool IsDisposed => DisposeCallCount > 0;

            public int TouchCount => Volatile.Read(ref _touchCount);

            public void Dispose() => Interlocked.Increment(ref _disposeCallCount);

            public void Touch()
            {
                ObjectDisposedException.ThrowIf(IsDisposed, this);
                Interlocked.Increment(ref _touchCount);
            }
        }
    }
}
