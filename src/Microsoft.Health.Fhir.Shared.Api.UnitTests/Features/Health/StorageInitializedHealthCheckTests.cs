// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
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

    public StorageInitializedHealthCheckTests()
    {
        var storageHealthCheckStatusReporter = Substitute.For<IStorageHealthCheckStatusReporter>();
        _sut = new StorageInitializedHealthCheck(storageHealthCheckStatusReporter);
    }

    [Fact]
    public async Task GivenStorageInitialized_WhenCheckHealthAsync_ThenReturnsHealthy()
    {
        await _sut.Handle(new SearchParametersInitializedNotification(), CancellationToken.None);

        HealthCheckResult result = await _sut.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task GivenStorageInitializedHealthCheck_WhenCheckHealthAsync_ThenStartsAsDegraded()
    {
        HealthCheckResult result = await _sut.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);
        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    [Fact]
    public async Task GivenStorageInitilizedHealthCheck_WhenCMKUnhealthy_ThenReturnsDegraded()
    {
        // Arrange
        var degradedResult = HealthCheckResult.Degraded("Customer-managed key is degraded");

        var storageHealthCheckStatusReporter = Substitute.For<IStorageHealthCheckStatusReporter>();
        storageHealthCheckStatusReporter.IsHealthyAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(degradedResult));
        var sut = new StorageInitializedHealthCheck(storageHealthCheckStatusReporter);

        await sut.Handle(new SearchParametersInitializedNotification(), CancellationToken.None);

        // Act
        HealthCheckResult result = await sut.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        // Assert
        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("Customer-managed key is unhealthy", result.Description);
        Assert.NotNull(result.Exception);
    }

#if NET8_0_OR_GREATER
    [Fact]
    public async Task GivenStorageInitializedHealthCheck_WhenCheckHealthAsync_ThenChangedToUnhealthyAfter5Minute()
    {
        using (Mock.Property(() => ClockResolver.TimeProvider, new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.Now.AddMinutes(5).AddSeconds(1))))
        {
            HealthCheckResult result = await _sut.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);
            Assert.Equal(HealthStatus.Unhealthy, result.Status);
        }
    }
#endif
}
