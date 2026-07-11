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
using Microsoft.Health.Fhir.Core.Messages.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.Features.Health;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.DataSourceValidation)]
public class StorageInitializedHealthCheckTests
{
    private readonly StorageInitializedHealthCheck _sut;
    private readonly IDatabaseStatusReporter _databaseStatusReporter;

    public StorageInitializedHealthCheckTests()
    {
        _databaseStatusReporter = Substitute.For<IDatabaseStatusReporter>();
        _databaseStatusReporter.IsCustomerManagerKeyProperlySetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));
        _sut = CreateSut();
    }

    [Fact]
    public async Task GivenStorageInitialized_WhenCheckHealthAsync_ThenReturnsHealthy()
    {
        await _sut.Handle(new SearchParametersInitializedNotification(), CancellationToken.None);

        HealthCheckResult result = await _sut.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task GivenStorageInitializedHealthCheck_WhenCheckHealthAsync_ThenStartsAsUnhealthy()
    {
        HealthCheckResult result = await _sut.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task GivenStorageInitializedHealthCheck_WhenCheckHealthAsync_ThenChangedToUnhealthyAfter5Minutes()
    {
        using (Mock.Property(() => ClockResolver.TimeProvider, new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.Now.AddMinutes(5).AddSeconds(1))))
        {
            HealthCheckResult result = await _sut.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

            Assert.Equal(HealthStatus.Unhealthy, result.Status);
            Assert.Contains("Storage has not been initialized", result.Description);
        }
    }

    [Fact]
    public async Task GivenStartupDegradedDelayHasElapsed_WhenCheckHealthAsync_ThenReturnsDegraded()
    {
        var timeProvider = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.UtcNow);
        using (Mock.Property(() => ClockResolver.TimeProvider, timeProvider))
        {
            StorageInitializedHealthCheck sut = CreateSut(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(2));
            timeProvider.Advance(TimeSpan.FromMilliseconds(1));

            HealthCheckResult result = await sut.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

            Assert.Equal(HealthStatus.Degraded, result.Status);
            Assert.Contains("Storage is initializing", result.Description);
        }
    }

    [Fact]
    public async Task GivenStorageInitializationTimeoutHasElapsed_WhenCheckHealthAsync_ThenReturnsUnhealthy()
    {
        var timeProvider = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.UtcNow);
        using (Mock.Property(() => ClockResolver.TimeProvider, timeProvider))
        {
            StorageInitializedHealthCheck sut = CreateSut(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(2));
            timeProvider.Advance(TimeSpan.FromMilliseconds(2));

            HealthCheckResult result = await sut.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

            Assert.Equal(HealthStatus.Unhealthy, result.Status);
            Assert.Contains("Storage has not been initialized", result.Description);
        }
    }

    [Fact]
    public void GivenStartupDegradedDelayExceedsStorageInitializationTimeout_WhenCreatingHealthCheck_ThenThrows()
    {
        Assert.Throws<ArgumentException>(() => CreateSut(TimeSpan.FromMilliseconds(2), TimeSpan.FromMilliseconds(1)));
    }

    [Fact]
    public void GivenStartupDegradedDelayIsNegative_WhenCreatingHealthCheck_ThenThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateSut(TimeSpan.FromMilliseconds(-1), TimeSpan.FromMilliseconds(1)));
    }

    [Fact]
    public void GivenStorageInitializationTimeoutIsNegative_WhenCreatingHealthCheck_ThenThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateSut(TimeSpan.Zero, TimeSpan.FromMilliseconds(-1)));
    }

    [Fact]
    public async Task GivenUnhealthyCMK_WhenCheckHealthAsyncAfter5Minutes_ThenChangedToDegraded()
    {
        using (Mock.Property(() => ClockResolver.TimeProvider, new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.Now.AddMinutes(5).AddSeconds(1))))
        {
            // Arrange
            _databaseStatusReporter.IsCustomerManagerKeyProperlySetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(false));

            HealthCheckResult result = await _sut.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);
            Assert.Equal(HealthStatus.Degraded, result.Status);
            Assert.Contains("The health of the store has degraded. Customer-managed key is not properly set.", result.Description);
        }
    }

    private StorageInitializedHealthCheck CreateSut(
        TimeSpan? startupDegradedDelay = null,
        TimeSpan? storageInitializationTimeout = null)
    {
        return new StorageInitializedHealthCheck(
            _databaseStatusReporter,
            Options.Create(
                new StorageInitializedHealthCheckConfiguration
                {
                    StartupDegradedDelay = startupDegradedDelay ?? TimeSpan.FromMinutes(1),
                    StorageInitializationTimeout = storageInitializationTimeout ?? TimeSpan.FromMinutes(5),
                }));
    }
}
