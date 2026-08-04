// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Health.Fhir.Core.Features.Tenancy;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Tenancy
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class TenantContainerTests
    {
        private static readonly TenantDescriptor Tenant = new(new TenantId("alpha"));

        [Fact]
        public async Task GivenAnOpenContainer_WhenALeaseIsAcquired_ThenServicesAreResolvable()
        {
            var services = new ServiceCollection();
            services.AddSingleton<SampleService>();

            await using var container = CreateContainer(services, new FakeTimeProvider());

            Assert.True(container.TryAcquire(out ITenantLease lease));

            using (lease)
            {
                Assert.NotNull(lease.Services.GetRequiredService<SampleService>());
                Assert.Equal(Tenant.TenantId, lease.TenantId);
            }
        }

        [Fact]
        public async Task GivenALease_WhenDisposedTwice_ThenTheRefCountOnlyDropsOnce()
        {
            await using var container = CreateContainer(new ServiceCollection(), new FakeTimeProvider());

            Assert.True(container.TryAcquire(out ITenantLease lease));
            Assert.Equal(1, container.ActiveLeaseCount);

            lease.Dispose();
            lease.Dispose();

            Assert.Equal(0, container.ActiveLeaseCount);
        }

        [Fact]
        public async Task GivenAContainerWithAnInFlightLease_WhenDisposed_ThenDisposalWaitsForTheLease()
        {
            var services = new ServiceCollection();
            services.AddSingleton<RecordingDisposable>();

            var container = CreateContainer(services, new FakeTimeProvider());

            Assert.True(container.TryAcquire(out ITenantLease lease));
            RecordingDisposable disposable = lease.Services.GetRequiredService<RecordingDisposable>();

            ValueTask disposal = container.DisposeAsync();

            Assert.False(disposal.IsCompleted);
            Assert.False(disposable.Disposed);

            lease.Dispose();

            await disposal;

            Assert.True(disposable.Disposed);
        }

        [Fact]
        public async Task GivenADrainingContainer_WhenALeaseIsRequested_ThenAcquisitionFails()
        {
            var container = CreateContainer(new ServiceCollection(), new FakeTimeProvider());

            Assert.True(container.TryAcquire(out ITenantLease held));

            ValueTask disposal = container.DisposeAsync();

            Assert.False(container.TryAcquire(out ITenantLease rejected));
            Assert.Null(rejected);

            held.Dispose();
            await disposal;
        }

        [Fact]
        public async Task GivenConcurrentDisposals_WhenTeardownCompletes_ThenTheyShareTheSameOperation()
        {
            var services = new ServiceCollection();
            services.AddSingleton<RecordingDisposable>();
            var container = CreateContainer(services, new FakeTimeProvider());

            Assert.True(container.TryAcquire(out ITenantLease lease));
            RecordingDisposable disposable = lease.Services.GetRequiredService<RecordingDisposable>();

            Task first = container.DisposeAsync().AsTask();
            Task second = container.DisposeAsync().AsTask();

            Assert.Same(first, second);
            Assert.False(first.IsCompleted);

            lease.Dispose();

            await Task.WhenAll(first, second);

            Assert.True(disposable.Disposed);
        }

        [Fact]
        public async Task GivenAContainerWithInitializers_WhenStarted_ThenEachInitializerIsStartedAndStoppedInReverseOrder()
        {
            var events = new List<string>();
            var services = new ServiceCollection();
            var first = new RecordingHostedService("first", events);
            var second = new RecordingHostedService("second", events);
            services.AddSingleton<IHostedService>(first);
            services.AddSingleton<IHostedService>(second);

            var container = CreateContainer(services, new FakeTimeProvider());
            await container.StartInitializersAsync(CancellationToken.None);

            Assert.True(first.Started);
            Assert.True(second.Started);
            Assert.False(first.Stopped);
            Assert.False(second.Stopped);
            Assert.Equal(new[] { "start:first", "start:second" }, events);

            await container.DisposeAsync();

            Assert.True(first.Stopped);
            Assert.True(second.Stopped);
            Assert.Equal(
                new[] { "start:first", "start:second", "stop:second", "stop:first" },
                events);
        }

        [Fact]
        public async Task GivenAContainerWithAFailingInitializerStop_WhenDisposed_ThenTheProviderIsStillDisposed()
        {
            RecordingDisposable disposable = null;
            var services = new ServiceCollection();
            services.AddSingleton(_ => disposable = new RecordingDisposable());
            services.AddSingleton<ThrowingHostedService>();
            services.AddSingleton<IHostedService>(serviceProvider => serviceProvider.GetRequiredService<ThrowingHostedService>());

            var container = CreateContainer(services, new FakeTimeProvider());
            await container.StartInitializersAsync(CancellationToken.None);

            await Assert.ThrowsAsync<InvalidOperationException>(async () => await container.DisposeAsync());

            Assert.NotNull(disposable);
            Assert.True(disposable.Disposed);
        }

        [Fact]
        public async Task GivenConcurrentDisposals_WhenTeardownFails_ThenTheyObserveTheSameFailure()
        {
            RecordingDisposable disposable = null;
            var events = new EventRecorder();
            BlockingThrowingStopHostedService initializer = null;
            var services = new ServiceCollection();
            services.AddSingleton(_ => disposable = new RecordingDisposable());
            services.AddSingleton<IHostedService>(serviceProvider => initializer = new BlockingThrowingStopHostedService(
                events,
                serviceProvider.GetRequiredService<RecordingDisposable>()));

            var container = CreateContainer(services, new FakeTimeProvider());
            await container.StartInitializersAsync(CancellationToken.None);

            Task first = container.DisposeAsync().AsTask();
            Assert.NotNull(initializer);
            await initializer.StopEntered.Task;

            Task second = container.DisposeAsync().AsTask();
            Assert.Same(first, second);
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);

            initializer.AllowStopFailure.TrySetResult(true);

            InvalidOperationException firstException = await Assert.ThrowsAsync<InvalidOperationException>(async () => await first);
            InvalidOperationException secondException = await Assert.ThrowsAsync<InvalidOperationException>(async () => await second);

            Assert.Equal("stop failed", firstException.Message);
            Assert.Equal(firstException.Message, secondException.Message);
            Assert.NotNull(disposable);
            Assert.True(disposable.Disposed);
        }

        [Fact]
        public async Task GivenMultipleTeardownFailures_WhenDisposed_ThenAllFailuresAreReportedAfterAllCleanupIsAttempted()
        {
            var events = new EventRecorder();
            var services = new ServiceCollection();
            services.AddSingleton(events);
            services.AddSingleton<ThrowingDisposableResource>();
            services.AddSingleton<IHostedService>(serviceProvider => new ThrowingStopHostedService("first", events, serviceProvider.GetRequiredService<ThrowingDisposableResource>()));
            services.AddSingleton<IHostedService>(serviceProvider => new ThrowingStopHostedService("second", events, serviceProvider.GetRequiredService<ThrowingDisposableResource>()));

            var container = CreateContainer(services, new FakeTimeProvider());
            await container.StartInitializersAsync(CancellationToken.None);

            AggregateException exception = await Assert.ThrowsAsync<AggregateException>(async () => await container.DisposeAsync());

            Assert.Equal(
                new[] { "stop:second", "stop:second:throw", "stop:first", "stop:first:throw", "dispose:resource" },
                events.Snapshot());
            Assert.Equal(3, exception.InnerExceptions.Count);
            Assert.Equal(
                new[] { "stop:second", "stop:first", "dispose:resource" },
                exception.InnerExceptions.Select(static e => e.Message).ToArray());
        }

        [Fact]
        public async Task GivenStartupInProgress_WhenDisposed_ThenStartupFinishesBeforeTeardownAndLaterStartupIsRejected()
        {
            var events = new EventRecorder();
            var initializer = new BlockingStartHostedService(events);
            var services = new ServiceCollection();
            services.AddSingleton<IHostedService>(initializer);

            var container = CreateContainer(services, new FakeTimeProvider());
            Task startup = container.StartInitializersAsync(CancellationToken.None);

            await initializer.StartEntered.Task;

            Task disposal = container.DisposeAsync().AsTask();

            Assert.False(disposal.IsCompleted);
            await Assert.ThrowsAsync<InvalidOperationException>(() => container.StartInitializersAsync(CancellationToken.None));

            initializer.AllowStartCompletion.TrySetResult(true);

            await startup;
            await disposal;

            Assert.Equal(new[] { "start:entered", "start:completed", "stop" }, events.Snapshot());
            Assert.Equal(1, initializer.StartCallCount);
            Assert.Equal(1, initializer.StopCallCount);
        }

        [Fact]
        public async Task GivenRepeatedOrConcurrentStartupCalls_WhenInitializersStart_ThenStartupRunsOnlyOnce()
        {
            var events = new EventRecorder();
            var initializer = new BlockingStartHostedService(events);
            var services = new ServiceCollection();
            services.AddSingleton<IHostedService>(initializer);

            await using var container = CreateContainer(services, new FakeTimeProvider());

            Task first = container.StartInitializersAsync(CancellationToken.None);
            await initializer.StartEntered.Task;

            Task second = container.StartInitializersAsync(CancellationToken.None);

            Assert.Same(first, second);
            Assert.Equal(1, initializer.StartCallCount);

            initializer.AllowStartCompletion.TrySetResult(true);

            await Task.WhenAll(first, second);

            Task third = container.StartInitializersAsync(CancellationToken.None);

            Assert.Same(first, third);
            Assert.Equal(1, initializer.StartCallCount);
        }

        [Fact]
        public async Task GivenAFailedInitializerStartup_WhenALeaseIsRequested_ThenAcquisitionIsRejected()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IHostedService>(new ThrowingStartHostedService());

            var container = CreateContainer(services, new FakeTimeProvider());

            await Assert.ThrowsAsync<InvalidOperationException>(() => container.StartInitializersAsync(CancellationToken.None));

            Assert.False(container.TryAcquire(out ITenantLease lease));
            Assert.Null(lease);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await container.DisposeAsync());
        }

        [Fact]
        public async Task GivenStartupCancellationDuringDisposal_WhenTeardownWaitsForStartup_ThenTheContainerBecomesTerminalAndCleansUp()
        {
            RecordingDisposable disposable = null;
            var events = new EventRecorder();
            var cancellationTokenSource = new CancellationTokenSource();
            CancellableBlockingStartHostedService blockingInitializer = null;
            var services = new ServiceCollection();
            services.AddSingleton(_ => disposable = new RecordingDisposable());
            services.AddSingleton<IHostedService>(serviceProvider => new EventingHostedService(
                "first",
                events,
                serviceProvider.GetRequiredService<RecordingDisposable>()));
            services.AddSingleton<IHostedService>(serviceProvider => blockingInitializer = new CancellableBlockingStartHostedService(
                "second",
                events,
                serviceProvider.GetRequiredService<RecordingDisposable>()));

            var container = CreateContainer(services, new FakeTimeProvider());
            Task startup = container.StartInitializersAsync(cancellationTokenSource.Token);

            Assert.NotNull(blockingInitializer);
            await blockingInitializer.StartEntered.Task;

            Task disposal = container.DisposeAsync().AsTask();

            Assert.False(disposal.IsCompleted);

            cancellationTokenSource.Cancel();

            OperationCanceledException startupException =
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await startup);
            Assert.Equal(cancellationTokenSource.Token, startupException.CancellationToken);

            OperationCanceledException disposalException =
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await disposal);
            Assert.Equal(cancellationTokenSource.Token, disposalException.CancellationToken);

            Assert.False(container.TryAcquire(out ITenantLease lease));
            Assert.Null(lease);
            await Assert.ThrowsAsync<InvalidOperationException>(() => container.StartInitializersAsync(CancellationToken.None));

            Assert.Equal(
                new[] { "start:first", "start:second:entered", "start:second:canceled", "stop:first" },
                events.Snapshot());
            Assert.Equal(1, blockingInitializer.StartCallCount);
            Assert.Equal(0, blockingInitializer.StopCallCount);
            Assert.NotNull(disposable);
            Assert.True(disposable.Disposed);
        }

        [Fact]
        public async Task GivenAContainer_WhenALeaseIsAcquired_ThenLastAccessedIsUpdated()
        {
            var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero));
            await using var container = CreateContainer(new ServiceCollection(), timeProvider);

            DateTimeOffset initial = container.LastAccessedUtc;

            timeProvider.Advance(TimeSpan.FromMinutes(5));

            Assert.True(container.TryAcquire(out ITenantLease lease));
            lease.Dispose();

            Assert.Equal(initial.AddMinutes(5), container.LastAccessedUtc);
        }

        private static TenantContainer CreateContainer(IServiceCollection services, TimeProvider timeProvider)
        {
            ServiceProvider provider = services.BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false });

            return new TenantContainer(Tenant, provider, timeProvider);
        }

        private sealed class SampleService
        {
        }

        private sealed class RecordingDisposable : IDisposable
        {
            public bool Disposed { get; private set; }

            public void Dispose() => Disposed = true;
        }

        private sealed class RecordingHostedService : IHostedService
        {
            private readonly string _name;
            private readonly IList<string> _events;

            public RecordingHostedService(string name, IList<string> events)
            {
                _name = name;
                _events = events;
            }

            public bool Started { get; private set; }

            public bool Stopped { get; private set; }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                Started = true;
                _events.Add($"start:{_name}");
                return Task.CompletedTask;
            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                Stopped = true;
                _events.Add($"stop:{_name}");
                return Task.CompletedTask;
            }
        }

        private sealed class ThrowingHostedService : IHostedService
        {
            public ThrowingHostedService(RecordingDisposable disposable)
            {
            }

            public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            public Task StopAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("stop failed");
        }

        private sealed class ThrowingDisposableResource : IDisposable
        {
            private readonly EventRecorder _events;

            public ThrowingDisposableResource(EventRecorder events)
            {
                _events = events;
            }

            public void Dispose()
            {
                _events.Record("dispose:resource");
                throw new InvalidOperationException("dispose:resource");
            }
        }

        private sealed class ThrowingStopHostedService : IHostedService
        {
            private readonly string _name;
            private readonly EventRecorder _events;

            public ThrowingStopHostedService(string name, EventRecorder events, ThrowingDisposableResource resource)
            {
                _name = name;
                _events = events;
            }

            public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            public Task StopAsync(CancellationToken cancellationToken)
            {
                _events.Record($"stop:{_name}");
                _events.Record($"stop:{_name}:throw");
                throw new InvalidOperationException($"stop:{_name}");
            }
        }

        private sealed class BlockingStartHostedService : IHostedService
        {
            private readonly EventRecorder _events;
            private int _startCallCount;
            private int _stopCallCount;

            public BlockingStartHostedService(EventRecorder events)
            {
                _events = events;
            }

            public int StartCallCount => Volatile.Read(ref _startCallCount);

            public int StopCallCount => Volatile.Read(ref _stopCallCount);

            public TaskCompletionSource<bool> AllowStartCompletion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> StartEntered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public async Task StartAsync(CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _startCallCount);
                _events.Record("start:entered");
                StartEntered.TrySetResult(true);
                await AllowStartCompletion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                _events.Record("start:completed");
            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _stopCallCount);
                _events.Record("stop");
                return Task.CompletedTask;
            }
        }

        private sealed class ThrowingStartHostedService : IHostedService
        {
            public Task StartAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("start failed");

            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }

        private sealed class BlockingThrowingStopHostedService : IHostedService
        {
            private readonly EventRecorder _events;

            public BlockingThrowingStopHostedService(EventRecorder events, RecordingDisposable disposable)
            {
                _events = events;
            }

            public TaskCompletionSource<bool> AllowStopFailure { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> StopEntered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            public async Task StopAsync(CancellationToken cancellationToken)
            {
                _events.Record("stop:entered");
                StopEntered.TrySetResult(true);
                await AllowStopFailure.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                _events.Record("stop:throw");
                throw new InvalidOperationException("stop failed");
            }
        }

        private sealed class EventingHostedService : IHostedService
        {
            private readonly string _name;
            private readonly EventRecorder _events;

            public EventingHostedService(string name, EventRecorder events, RecordingDisposable disposable)
            {
                _name = name;
                _events = events;
            }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                _events.Record($"start:{_name}");
                return Task.CompletedTask;
            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                _events.Record($"stop:{_name}");
                return Task.CompletedTask;
            }
        }

        private sealed class CancellableBlockingStartHostedService : IHostedService
        {
            private readonly string _name;
            private readonly EventRecorder _events;
            private int _startCallCount;
            private int _stopCallCount;

            public CancellableBlockingStartHostedService(string name, EventRecorder events, RecordingDisposable disposable)
            {
                _name = name;
                _events = events;
            }

            public int StartCallCount => Volatile.Read(ref _startCallCount);

            public int StopCallCount => Volatile.Read(ref _stopCallCount);

            public TaskCompletionSource<bool> StartEntered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public async Task StartAsync(CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _startCallCount);
                _events.Record($"start:{_name}:entered");
                StartEntered.TrySetResult(true);

                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    _events.Record($"start:{_name}:canceled");
                    throw;
                }
            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _stopCallCount);
                _events.Record($"stop:{_name}");
                return Task.CompletedTask;
            }
        }

        private sealed class EventRecorder
        {
            private readonly ConcurrentQueue<string> _events = new();

            public void Record(string value) => _events.Enqueue(value);

            public string[] Snapshot() => _events.ToArray();
        }
    }
}
