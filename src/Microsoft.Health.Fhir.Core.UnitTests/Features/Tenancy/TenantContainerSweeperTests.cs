// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Health.Fhir.Core.Features.Tenancy;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Tenancy
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class TenantContainerSweeperTests
    {
        private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

        [Fact]
        public async Task GivenASweepInterval_WhenTheTimerTicks_ThenTheCacheIsSwept()
        {
            var cache = Substitute.For<ITenantContainerCache>();
            var timeProvider = CreateTimeProvider();
            var sweepObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            cache.EvictIdleAsync(Arg.Any<CancellationToken>()).Returns(_ =>
            {
                sweepObserved.TrySetResult();
                return ValueTask.CompletedTask;
            });

            var sut = CreateSut(cache, timeProvider, Substitute.For<ILogger<TenantContainerSweeper>>());

            await sut.StartAsync(CancellationToken.None);
            await WaitForTimerAsync(timeProvider);

            timeProvider.Advance(SweepInterval);

            await sweepObserved.Task.WaitAsync(WaitTimeout);
            await sut.StopAsync(CancellationToken.None);

            await cache.Received(1).EvictIdleAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenASweepFailure_WhenTheNextTickArrives_ThenTheFailureIsLoggedAndTheSweepIsRetried()
        {
            var cache = Substitute.For<ITenantContainerCache>();
            var logger = Substitute.For<ILogger<TenantContainerSweeper>>();
            var timeProvider = CreateTimeProvider();
            var firstSweepObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondSweepObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var failure = new InvalidOperationException("boom");
            int callCount = 0;

            cache.EvictIdleAsync(Arg.Any<CancellationToken>()).Returns(_ =>
            {
                int currentCall = Interlocked.Increment(ref callCount);

                if (currentCall == 1)
                {
                    firstSweepObserved.TrySetResult();
                    return ValueTask.FromException(failure);
                }

                secondSweepObserved.TrySetResult();
                return ValueTask.CompletedTask;
            });

            var sut = CreateSut(cache, timeProvider, logger);

            await sut.StartAsync(CancellationToken.None);
            await WaitForTimerAsync(timeProvider);

            timeProvider.Advance(SweepInterval);
            await firstSweepObserved.Task.WaitAsync(WaitTimeout);

            timeProvider.Advance(SweepInterval);
            await secondSweepObserved.Task.WaitAsync(WaitTimeout);

            await sut.StopAsync(CancellationToken.None);

            Assert.Equal(2, callCount);
            logger.Received(1).Log(
                LogLevel.Warning,
                Arg.Any<EventId>(),
                Arg.Is<object>(state => state.ToString().Contains("Tenant container sweep failed. It will be retried.")),
                failure,
                Arg.Any<Func<object, Exception, string>>());
        }

        [Fact]
        public async Task GivenAServiceWaitingForTheNextTick_WhenStopIsRequested_ThenCancellationIsHandledCleanly()
        {
            var cache = Substitute.For<ITenantContainerCache>();
            var timeProvider = CreateTimeProvider();
            var sut = CreateSut(cache, timeProvider, Substitute.For<ILogger<TenantContainerSweeper>>());

            await sut.StartAsync(CancellationToken.None);
            await WaitForTimerAsync(timeProvider);
            await sut.StopAsync(CancellationToken.None);

            timeProvider.Advance(SweepInterval);
            await Task.Delay(50);

            await cache.DidNotReceiveWithAnyArgs().EvictIdleAsync(default);
        }

        private static TenantContainerSweeper CreateSut(
            ITenantContainerCache cache,
            TimeProvider timeProvider,
            ILogger<TenantContainerSweeper> logger)
        {
            return new TenantContainerSweeper(
                cache,
                Options.Create(new TenantContainerCacheOptions
                {
                    MaxResidentTenants = 5,
                    IdleTimeout = IdleTimeout,
                    SweepInterval = SweepInterval,
                }),
                timeProvider,
                logger);
        }

        private static async Task WaitForTimerAsync(ObservableFakeTimeProvider timeProvider)
        {
            await timeProvider.TimerCreated.WaitAsync(WaitTimeout);
        }

        private static ObservableFakeTimeProvider CreateTimeProvider()
        {
            return new ObservableFakeTimeProvider(
                new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero));
        }

        private sealed class ObservableFakeTimeProvider : FakeTimeProvider
        {
            private readonly TaskCompletionSource _timerCreated =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public ObservableFakeTimeProvider(DateTimeOffset startDateTime)
                : base(startDateTime)
            {
            }

            public Task TimerCreated => _timerCreated.Task;

            public override ITimer CreateTimer(
                TimerCallback callback,
                object state,
                TimeSpan dueTime,
                TimeSpan period)
            {
                ITimer timer = base.CreateTimer(callback, state, dueTime, period);
                _timerCreated.TrySetResult();
                return timer;
            }
        }
    }
}
