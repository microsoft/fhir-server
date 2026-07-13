// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.Features.Health;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.DataSourceValidation)]
public class StorageInitializedHealthCheckTests
{
    [Fact]
    public async Task GivenStorageInitialized_WhenCheckHealthAsync_ThenReturnsHealthy()
    {
        StorageInitializedHealthCheck sut = CreateSut();

        await sut.Handle(new SearchParametersInitializedNotification(), CancellationToken.None);

        HealthCheckResult result = await sut.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task GivenNotInitialized_WhenCheckHealthAsyncBeforeTimeout_ThenReturnsUnhealthy()
    {
        StorageInitializedHealthCheck sut = CreateSut();

        HealthCheckResult result = await sut.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("Storage is initializing", result.Description);
    }

    [Fact]
    public async Task GivenNotInitializedAndTimeoutElapsed_WhenCheckHealthAsync_ThenReturnsHealthyBackstop()
    {
        var timeProvider = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.UtcNow);
        using (Mock.Property(() => ClockResolver.TimeProvider, timeProvider))
        {
            StorageInitializedHealthCheck sut = CreateSut(TimeSpan.FromMilliseconds(1));
            timeProvider.Advance(TimeSpan.FromMilliseconds(2));

            HealthCheckResult result = await sut.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

            Assert.Equal(HealthStatus.Healthy, result.Status);
        }
    }

    [Fact]
    public async Task GivenNotInitializedAtExactBoundary_WhenCheckHealthAsync_ThenReturnsHealthyBackstop()
    {
        var timeProvider = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.UtcNow);
        using (Mock.Property(() => ClockResolver.TimeProvider, timeProvider))
        {
            StorageInitializedHealthCheck sut = CreateSut(TimeSpan.FromMilliseconds(2));
            timeProvider.Advance(TimeSpan.FromMilliseconds(2));

            HealthCheckResult result = await sut.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

            Assert.Equal(HealthStatus.Healthy, result.Status);
        }
    }

    [Fact]
    public async Task GivenConcurrentNotificationAndProbes_WhenCheckHealthAsync_ThenHandoffIsObserved()
    {
        StorageInitializedHealthCheck sut = CreateSut();
        using var cts = new CancellationTokenSource();

        Task reader = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                await sut.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);
            }
        });

        await sut.Handle(new SearchParametersInitializedNotification(), CancellationToken.None);
        HealthCheckResult result = await sut.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);
        cts.Cancel();
        await reader;

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public void GivenDefaultConfiguration_ThenStorageInitializationTimeoutMatchesDocumentedInvariant()
    {
        // Guards the K8s-budget invariant: the documented app timeout is 5 minutes.
        // If this default changes, the fhir-paas startup budget must be re-checked (must stay strictly greater).
        Assert.Equal(TimeSpan.FromMinutes(5), new StorageInitializedHealthCheckConfiguration().StorageInitializationTimeout);
    }

    private static StorageInitializedHealthCheck CreateSut(TimeSpan? storageInitializationTimeout = null)
    {
        return new StorageInitializedHealthCheck(
            Options.Create(
                new StorageInitializedHealthCheckConfiguration
                {
                    StorageInitializationTimeout = storageInitializationTimeout ?? TimeSpan.FromMinutes(5),
                }));
    }
}
