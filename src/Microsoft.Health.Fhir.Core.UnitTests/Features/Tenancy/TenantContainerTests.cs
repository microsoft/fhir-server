// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
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
        public async Task GivenAContainer_WhenDisposedTwice_ThenTheSecondDisposalIsANoOp()
        {
            var container = CreateContainer(new ServiceCollection(), new FakeTimeProvider());

            await container.DisposeAsync();
            await container.DisposeAsync();
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
    }
}
