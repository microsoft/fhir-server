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

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Tenancy
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class TenantContainerCacheTests
    {
        private readonly FakeTimeProvider _timeProvider =
            new(new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero));

        private readonly RecordingFactory _factory;

        public TenantContainerCacheTests()
        {
            _factory = new RecordingFactory(_timeProvider);
        }

        [Fact]
        public void GivenDefaultOptions_WhenRead_ThenTheContractDefaultsAreReturned()
        {
            var options = new TenantContainerCacheOptions();

            Assert.Equal(100, options.MaxResidentTenants);
            Assert.Equal(TimeSpan.FromMinutes(30), options.IdleTimeout);
            Assert.Equal(TimeSpan.FromMinutes(1), options.SweepInterval);
        }

        [Fact]
        public async Task GivenAnEmptyCache_WhenATenantIsAcquired_ThenAContainerIsCreated()
        {
            await using TenantContainerCache cache = CreateCache();

            using (await cache.AcquireAsync(Tenant("alpha"), CancellationToken.None))
            {
                Assert.Equal(1, cache.Count);
            }

            Assert.Equal(1, _factory.CreateCount);
        }

        [Fact]
        public async Task GivenACachedTenant_WhenAcquiredAgain_ThenTheContainerIsReused()
        {
            await using TenantContainerCache cache = CreateCache();

            using (await cache.AcquireAsync(Tenant("alpha"), CancellationToken.None))
            {
            }

            using (await cache.AcquireAsync(Tenant("alpha"), CancellationToken.None))
            {
            }

            Assert.Equal(1, _factory.CreateCount);
            Assert.Equal(1, cache.Count);
        }

        [Fact]
        public async Task GivenACachedTenant_WhenTheAcquisitionIsCanceled_ThenNoLeaseIsHandedOut()
        {
            await using TenantContainerCache cache = CreateCache();
            (await cache.AcquireAsync(Tenant("alpha"), CancellationToken.None)).Dispose();
            RecordingContainer container = Assert.Single(_factory.CreatedContainers);
            using var cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.Cancel();
            OperationCanceledException exception = null;
            ITenantLease unexpectedLease = null;

            try
            {
                unexpectedLease = await cache.AcquireAsync(Tenant("alpha"), cancellationTokenSource.Token);
            }
            catch (OperationCanceledException caught)
            {
                exception = caught;
            }
            finally
            {
                unexpectedLease?.Dispose();
            }

            Assert.NotNull(exception);
            Assert.Equal(cancellationTokenSource.Token, exception.CancellationToken);
            Assert.Equal(0, container.ActiveLeaseCount);
            Assert.Equal(1, _factory.CreateCount);
        }

        [Fact]
        public async Task GivenConcurrentFirstRequests_WhenTheSameTenantIsAcquired_ThenOnlyOneContainerIsBuilt()
        {
            var createEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowCreate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _factory.CreateHandler = async (tenant, cancellationToken) =>
            {
                createEntered.TrySetResult(true);
                await allowCreate.Task.WaitAsync(cancellationToken);
                return _factory.CreateContainer(tenant);
            };

            await using TenantContainerCache cache = CreateCache();

            Task<ITenantLease>[] acquisitions = Enumerable.Range(0, 16)
                .Select(_ => cache.AcquireAsync(Tenant("alpha"), CancellationToken.None).AsTask())
                .ToArray();

            await createEntered.Task;
            allowCreate.TrySetResult(true);

            ITenantLease[] leases = await Task.WhenAll(acquisitions);

            foreach (ITenantLease lease in leases)
            {
                lease.Dispose();
            }

            Assert.Equal(1, _factory.CreateCount);
        }

        [Fact]
        public async Task GivenAnIdleTenant_WhenTheTtlElapses_ThenItIsEvicted()
        {
            await using TenantContainerCache cache = CreateCache(idleTimeout: TimeSpan.FromMinutes(10));

            (await cache.AcquireAsync(Tenant("alpha"), CancellationToken.None)).Dispose();

            _timeProvider.Advance(TimeSpan.FromMinutes(11));

            await cache.EvictIdleAsync(CancellationToken.None);

            Assert.Equal(0, cache.Count);
            Assert.Equal(1, cache.EvictionCount);
            Assert.Equal(1, Assert.Single(_factory.CreatedContainers).DisposeCallCount);
        }

        [Fact]
        public async Task GivenAnActiveTenant_WhenTheTtlElapses_ThenItIsNotEvicted()
        {
            await using TenantContainerCache cache = CreateCache(idleTimeout: TimeSpan.FromMinutes(10));
            using ITenantLease lease = await cache.AcquireAsync(Tenant("alpha"), CancellationToken.None);

            _timeProvider.Advance(TimeSpan.FromMinutes(11));

            await cache.EvictIdleAsync(CancellationToken.None);

            Assert.Equal(1, cache.Count);
            Assert.Equal(0, cache.EvictionCount);
            Assert.Equal(0, Assert.Single(_factory.CreatedContainers).DisposeCallCount);
        }

        [Fact]
        public async Task GivenAFullCacheWithAnIdleTenant_WhenANewTenantArrives_ThenTheLeastRecentlyUsedIsEvicted()
        {
            await using TenantContainerCache cache = CreateCache(maxResidentTenants: 2);

            (await cache.AcquireAsync(Tenant("alpha"), CancellationToken.None)).Dispose();
            _timeProvider.Advance(TimeSpan.FromMinutes(1));
            (await cache.AcquireAsync(Tenant("beta"), CancellationToken.None)).Dispose();
            _timeProvider.Advance(TimeSpan.FromMinutes(1));

            (await cache.AcquireAsync(Tenant("gamma"), CancellationToken.None)).Dispose();

            Assert.Equal(2, cache.Count);
            Assert.Equal(1, cache.EvictionCount);
            Assert.Equal(
                1,
                _factory.CreatedContainers.Single(container => container.TenantId == new TenantId("alpha")).DisposeCallCount);

            (await cache.AcquireAsync(Tenant("alpha"), CancellationToken.None)).Dispose();

            Assert.Equal(4, _factory.CreateCount);
            Assert.Equal(2, cache.Count);
        }

        [Fact]
        public async Task GivenAFullCacheWithAllTenantsBusy_WhenANewTenantArrives_ThenAdmissionIsRejectedWithoutBuildingIt()
        {
            await using TenantContainerCache cache = CreateCache(maxResidentTenants: 2);
            using ITenantLease alpha = await cache.AcquireAsync(Tenant("alpha"), CancellationToken.None);
            using ITenantLease beta = await cache.AcquireAsync(Tenant("beta"), CancellationToken.None);

            TenantAdmissionRejectedException exception =
                await Assert.ThrowsAsync<TenantAdmissionRejectedException>(
                    () => cache.AcquireAsync(Tenant("gamma"), CancellationToken.None).AsTask());

            Assert.Equal(new TenantId("gamma"), exception.TenantId);
            Assert.Equal(2, exception.MaxResidentTenants);
            Assert.Equal(2, cache.MaxResidentTenants);
            Assert.Equal(1, cache.AdmissionRejectionCount);
            Assert.Equal(2, _factory.CreateCount);
            Assert.Equal(0, _factory.CreateCountFor(new TenantId("gamma")));
        }

        [Fact]
        public async Task GivenANewContainer_WhenItIsAdmitted_ThenItsInitialLeaseIsTakenBeforePublication()
        {
            TenantContainerCache cache = CreateCache();
            _factory.ConfigureContainer = container => container.CacheCountObserver = () => cache.Count;

            await using (cache)
            {
                using ITenantLease lease = await cache.AcquireAsync(Tenant("alpha"), CancellationToken.None);
                RecordingContainer container = Assert.Single(_factory.CreatedContainers);

                Assert.Equal(0, container.CacheCountAtFirstAcquire);
                Assert.Equal(1, cache.Count);
                Assert.Equal(1, container.ActiveLeaseCount);
            }
        }

        [Fact]
        public async Task GivenANewContainerThatRejectsItsInitialLease_WhenAdmissionFails_ThenItIsDisposedWithoutPublication()
        {
            _factory.ConfigureContainer = container => container.RejectLeases = true;
            await using TenantContainerCache cache = CreateCache();

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => cache.AcquireAsync(Tenant("alpha"), CancellationToken.None).AsTask());

            RecordingContainer container = Assert.Single(_factory.CreatedContainers);
            Assert.Equal(0, cache.Count);
            Assert.Equal(1, container.DisposeCallCount);
        }

        [Fact]
        public async Task GivenCreationInFlight_WhenCacheDisposalBegins_ThenTheContainerCannotPublishAndIsDisposed()
        {
            var createEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowCreate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _factory.CreateHandler = async (tenant, cancellationToken) =>
            {
                createEntered.TrySetResult(true);
                await allowCreate.Task.WaitAsync(cancellationToken);
                return _factory.CreateContainer(tenant);
            };

            TenantContainerCache cache = CreateCache();
            Task<ITenantLease> acquisition =
                cache.AcquireAsync(Tenant("alpha"), CancellationToken.None).AsTask();

            await createEntered.Task;

            Task disposal = cache.DisposeAsync().AsTask();

            Assert.False(disposal.IsCompleted);

            allowCreate.TrySetResult(true);

            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await acquisition);
            await disposal;

            RecordingContainer container = Assert.Single(_factory.CreatedContainers);
            Assert.Equal(0, cache.Count);
            Assert.Equal(1, container.TryAcquireCount);
            Assert.Equal(0, container.ActiveLeaseCount);
            Assert.Equal(1, container.DisposeCallCount);
        }

        [Fact]
        public async Task GivenCreationAndAnAdmissionWaiter_WhenCacheDisposalBegins_ThenBothAreRejectedBeforeSemaphoreDisposal()
        {
            var createEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowCreate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _factory.CreateHandler = async (tenant, cancellationToken) =>
            {
                createEntered.TrySetResult(true);
                await allowCreate.Task.WaitAsync(cancellationToken);
                return _factory.CreateContainer(tenant);
            };

            TenantContainerCache cache = CreateCache();
            Task<ITenantLease> alphaAcquisition =
                cache.AcquireAsync(Tenant("alpha"), CancellationToken.None).AsTask();
            await createEntered.Task;
            Task<ITenantLease> betaAcquisition =
                cache.AcquireAsync(Tenant("beta"), CancellationToken.None).AsTask();

            Task disposal = cache.DisposeAsync().AsTask();

            Assert.False(disposal.IsCompleted);

            allowCreate.TrySetResult(true);

            ObjectDisposedException alphaException =
                await Assert.ThrowsAsync<ObjectDisposedException>(async () => await alphaAcquisition);
            ObjectDisposedException betaException =
                await Assert.ThrowsAsync<ObjectDisposedException>(async () => await betaAcquisition);
            await disposal;

            Assert.Equal(nameof(TenantContainerCache), alphaException.ObjectName);
            Assert.Equal(nameof(TenantContainerCache), betaException.ObjectName);
            Assert.Equal(1, _factory.CreateCount);
            Assert.Equal(0, _factory.CreateCountFor(new TenantId("beta")));
            Assert.Equal(1, Assert.Single(_factory.CreatedContainers).DisposeCallCount);
        }

        [Fact]
        public async Task GivenACacheWithTenants_WhenDisposed_ThenEveryContainerIsDisposed()
        {
            TenantContainerCache cache = CreateCache();

            (await cache.AcquireAsync(Tenant("alpha"), CancellationToken.None)).Dispose();
            (await cache.AcquireAsync(Tenant("beta"), CancellationToken.None)).Dispose();

            await cache.DisposeAsync();

            Assert.Equal(0, cache.Count);
            Assert.All(_factory.CreatedContainers, container => Assert.Equal(1, container.DisposeCallCount));
        }

        [Fact]
        public async Task GivenConcurrentCacheDisposals_WhenContainerCleanupFails_ThenTheyShareCompletionAndFailure()
        {
            var failure = new InvalidOperationException("alpha disposal failed");
            _factory.ConfigureContainer = container =>
            {
                container.BlockDisposal();
                container.DisposalFailure = failure;
            };

            TenantContainerCache cache = CreateCache();
            (await cache.AcquireAsync(Tenant("alpha"), CancellationToken.None)).Dispose();
            RecordingContainer container = Assert.Single(_factory.CreatedContainers);

            Task first = cache.DisposeAsync().AsTask();
            await container.DisposalEntered.Task;

            Task second = cache.DisposeAsync().AsTask();

            Assert.Same(first, second);
            Assert.False(first.IsCompleted);

            container.AllowDisposal();

            InvalidOperationException firstException =
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await first);
            InvalidOperationException secondException =
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await second);

            Assert.Same(failure, firstException);
            Assert.Same(firstException, secondException);
            Assert.Equal(1, container.DisposeCallCount);
        }

        [Fact]
        public async Task GivenMultipleContainerCleanupFailures_WhenCacheIsDisposed_ThenAllCleanupIsAttemptedAndFailuresAreAggregated()
        {
            var alphaFailure = new InvalidOperationException("alpha disposal failed");
            var betaFailure = new InvalidOperationException("beta disposal failed");
            _factory.ConfigureContainer = container =>
            {
                if (container.TenantId == new TenantId("alpha"))
                {
                    container.DisposalFailure = alphaFailure;
                }
                else if (container.TenantId == new TenantId("beta"))
                {
                    container.DisposalFailure = betaFailure;
                }
            };

            TenantContainerCache cache = CreateCache();
            (await cache.AcquireAsync(Tenant("alpha"), CancellationToken.None)).Dispose();
            (await cache.AcquireAsync(Tenant("beta"), CancellationToken.None)).Dispose();
            (await cache.AcquireAsync(Tenant("gamma"), CancellationToken.None)).Dispose();

            AggregateException exception =
                await Assert.ThrowsAsync<AggregateException>(() => cache.DisposeAsync().AsTask());

            Assert.Collection(
                exception.InnerExceptions,
                inner => Assert.Same(alphaFailure, inner),
                inner => Assert.Same(betaFailure, inner));
            Assert.Equal(0, cache.Count);
            Assert.All(_factory.CreatedContainers, container => Assert.Equal(1, container.DisposeCallCount));
        }

        [Fact]
        public async Task GivenMultipleIdleCleanupFailures_WhenSwept_ThenAllCleanupIsAttemptedAndFailuresAreAggregated()
        {
            var alphaFailure = new InvalidOperationException("alpha eviction failed");
            var betaFailure = new InvalidOperationException("beta eviction failed");
            _factory.ConfigureContainer = container =>
            {
                if (container.TenantId == new TenantId("alpha"))
                {
                    container.DisposalFailure = alphaFailure;
                }
                else if (container.TenantId == new TenantId("beta"))
                {
                    container.DisposalFailure = betaFailure;
                }
            };

            await using TenantContainerCache cache = CreateCache(idleTimeout: TimeSpan.FromMinutes(10));
            (await cache.AcquireAsync(Tenant("alpha"), CancellationToken.None)).Dispose();
            (await cache.AcquireAsync(Tenant("beta"), CancellationToken.None)).Dispose();
            (await cache.AcquireAsync(Tenant("gamma"), CancellationToken.None)).Dispose();
            _timeProvider.Advance(TimeSpan.FromMinutes(11));

            AggregateException exception =
                await Assert.ThrowsAsync<AggregateException>(
                    () => cache.EvictIdleAsync(CancellationToken.None).AsTask());

            Assert.Collection(
                exception.InnerExceptions,
                inner => Assert.Same(alphaFailure, inner),
                inner => Assert.Same(betaFailure, inner));
            Assert.Equal(0, cache.Count);
            Assert.Equal(3, cache.EvictionCount);
            Assert.All(_factory.CreatedContainers, container => Assert.Equal(1, container.DisposeCallCount));
        }

        [Fact]
        public async Task GivenARequestWaitingForAdmission_WhenCanceled_ThenItDoesNotBuildTheIncomingTenant()
        {
            var createEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowCreate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _factory.CreateHandler = async (tenant, cancellationToken) =>
            {
                createEntered.TrySetResult(true);
                await allowCreate.Task.WaitAsync(cancellationToken);
                return _factory.CreateContainer(tenant);
            };

            await using TenantContainerCache cache = CreateCache();
            Task<ITenantLease> alphaAcquisition =
                cache.AcquireAsync(Tenant("alpha"), CancellationToken.None).AsTask();
            await createEntered.Task;

            using var cancellationTokenSource = new CancellationTokenSource();
            Task<ITenantLease> betaAcquisition =
                cache.AcquireAsync(Tenant("beta"), cancellationTokenSource.Token).AsTask();

            cancellationTokenSource.Cancel();

            OperationCanceledException exception =
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await betaAcquisition);

            Assert.Equal(cancellationTokenSource.Token, exception.CancellationToken);
            Assert.Equal(0, _factory.CreateCountFor(new TenantId("beta")));

            allowCreate.TrySetResult(true);
            (await alphaAcquisition).Dispose();

            Assert.Equal(1, _factory.CreateCount);
        }

        [Fact]
        public async Task GivenACanceledSweep_WhenIdleContainersExist_ThenNoContainerIsRemoved()
        {
            await using TenantContainerCache cache = CreateCache(idleTimeout: TimeSpan.FromMinutes(10));
            (await cache.AcquireAsync(Tenant("alpha"), CancellationToken.None)).Dispose();
            _timeProvider.Advance(TimeSpan.FromMinutes(11));
            using var cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.Cancel();

            OperationCanceledException exception =
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => cache.EvictIdleAsync(cancellationTokenSource.Token).AsTask());

            Assert.Equal(cancellationTokenSource.Token, exception.CancellationToken);
            Assert.Equal(1, cache.Count);
            Assert.Equal(0, cache.EvictionCount);
            Assert.Equal(0, Assert.Single(_factory.CreatedContainers).DisposeCallCount);
        }

        [Fact]
        public async Task GivenCancellationDuringIdleCleanup_WhenSwept_ThenOwnedCleanupCompletesBeforeCancellationIsReported()
        {
            await using TenantContainerCache cache = CreateCache(idleTimeout: TimeSpan.FromMinutes(10));
            (await cache.AcquireAsync(Tenant("alpha"), CancellationToken.None)).Dispose();
            RecordingContainer container = Assert.Single(_factory.CreatedContainers);
            container.BlockDisposal();
            _timeProvider.Advance(TimeSpan.FromMinutes(11));
            using var cancellationTokenSource = new CancellationTokenSource();

            Task sweep = cache.EvictIdleAsync(cancellationTokenSource.Token).AsTask();
            await container.DisposalEntered.Task;

            cancellationTokenSource.Cancel();
            container.AllowDisposal();

            OperationCanceledException exception =
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await sweep);

            Assert.Equal(cancellationTokenSource.Token, exception.CancellationToken);
            Assert.Equal(0, cache.Count);
            Assert.Equal(1, cache.EvictionCount);
            Assert.Equal(1, container.DisposeCallCount);
        }

        [Fact]
        public async Task GivenASweepInFlight_WhenCacheDisposalBegins_ThenDisposalWaitsForTheSweep()
        {
            await using TenantContainerCache cache = CreateCache(idleTimeout: TimeSpan.FromMinutes(10));
            (await cache.AcquireAsync(Tenant("alpha"), CancellationToken.None)).Dispose();
            RecordingContainer container = Assert.Single(_factory.CreatedContainers);
            container.BlockDisposal();
            _timeProvider.Advance(TimeSpan.FromMinutes(11));

            Task sweep = cache.EvictIdleAsync(CancellationToken.None).AsTask();
            await container.DisposalEntered.Task;

            Task disposal = cache.DisposeAsync().AsTask();

            Assert.False(disposal.IsCompleted);
            Assert.Equal(1, container.DisposeInvocationCount);

            container.AllowDisposal();

            await sweep;
            await disposal;

            Assert.Equal(0, cache.Count);
            Assert.Equal(1, container.DisposeCallCount);
        }

        [Fact]
        public async Task GivenADisposedCache_WhenAcquisitionOrSweepIsRequested_ThenNewWorkIsRejected()
        {
            TenantContainerCache cache = CreateCache();
            await cache.DisposeAsync();

            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => cache.AcquireAsync(Tenant("alpha"), CancellationToken.None).AsTask());
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => cache.EvictIdleAsync(CancellationToken.None).AsTask());

            Assert.Equal(0, _factory.CreateCount);
        }

        private static TenantDescriptor Tenant(string id) => new(new TenantId(id));

        private TenantContainerCache CreateCache(
            int maxResidentTenants = 50,
            TimeSpan? idleTimeout = null)
        {
            var options = new TenantContainerCacheOptions
            {
                MaxResidentTenants = maxResidentTenants,
                IdleTimeout = idleTimeout ?? TimeSpan.FromMinutes(30),
            };

            return new TenantContainerCache(_factory, Options.Create(options), _timeProvider);
        }

        private sealed class RecordingFactory : ITenantContainerFactory
        {
            private readonly ConcurrentDictionary<TenantId, int> _createCounts = new();
            private readonly ConcurrentQueue<RecordingContainer> _createdContainers = new();
            private readonly TimeProvider _timeProvider;
            private int _createCount;

            public RecordingFactory(TimeProvider timeProvider)
            {
                _timeProvider = timeProvider;
            }

            public Action<RecordingContainer> ConfigureContainer { get; set; }

            public int CreateCount => Volatile.Read(ref _createCount);

            public Func<TenantDescriptor, CancellationToken, ValueTask<ITenantContainer>> CreateHandler { get; set; }

            public RecordingContainer[] CreatedContainers => _createdContainers.ToArray();

            public ValueTask<ITenantContainer> CreateAsync(
                TenantDescriptor tenant,
                CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _createCount);
                _createCounts.AddOrUpdate(tenant.TenantId, 1, static (_, count) => count + 1);

                return CreateHandler is null
                    ? ValueTask.FromResult<ITenantContainer>(CreateContainer(tenant))
                    : CreateHandler(tenant, cancellationToken);
            }

            public RecordingContainer CreateContainer(TenantDescriptor tenant)
            {
                ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
                var container = new RecordingContainer(tenant, provider, _timeProvider);
                ConfigureContainer?.Invoke(container);
                _createdContainers.Enqueue(container);
                return container;
            }

            public int CreateCountFor(TenantId tenantId) =>
                _createCounts.TryGetValue(tenantId, out int count) ? count : 0;
        }

        private sealed class RecordingContainer : ITenantContainer
        {
            private readonly object _disposeSync = new();
            private readonly TenantContainer _inner;
            private TaskCompletionSource<bool> _allowDisposal;
            private Task _disposeTask;
            private int _disposeCallCount;
            private int _disposeInvocationCount;
            private int _tryAcquireCount;

            public RecordingContainer(
                TenantDescriptor tenant,
                ServiceProvider provider,
                TimeProvider timeProvider)
            {
                _inner = new TenantContainer(tenant, provider, timeProvider);
            }

            public int ActiveLeaseCount => _inner.ActiveLeaseCount;

            public Func<int> CacheCountObserver { get; set; }

            public int? CacheCountAtFirstAcquire { get; private set; }

            public int DisposeCallCount => Volatile.Read(ref _disposeCallCount);

            public int DisposeInvocationCount => Volatile.Read(ref _disposeInvocationCount);

            public TaskCompletionSource<bool> DisposalEntered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public Exception DisposalFailure { get; set; }

            public DateTimeOffset LastAccessedUtc => _inner.LastAccessedUtc;

            public bool RejectLeases { get; set; }

            public TenantId TenantId => _inner.TenantId;

            public int TryAcquireCount => Volatile.Read(ref _tryAcquireCount);

            public void AllowDisposal() => _allowDisposal?.TrySetResult(true);

            public void BlockDisposal()
            {
                _allowDisposal = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }

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

            public bool TryAcquire(out ITenantLease lease)
            {
                if (Interlocked.Increment(ref _tryAcquireCount) == 1 && CacheCountObserver is not null)
                {
                    CacheCountAtFirstAcquire = CacheCountObserver();
                }

                if (RejectLeases)
                {
                    lease = null;
                    return false;
                }

                return _inner.TryAcquire(out lease);
            }

            private async Task DisposeCoreAsync()
            {
                Interlocked.Increment(ref _disposeCallCount);
                DisposalEntered.TrySetResult(true);

                if (_allowDisposal is not null)
                {
                    await _allowDisposal.Task;
                }

                await _inner.DisposeAsync();

                if (DisposalFailure is not null)
                {
                    throw DisposalFailure;
                }
            }
        }
    }
}
