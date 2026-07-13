// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Api.Features.Health;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Registration;

// These tests assert the aggregate HealthStatus each probe endpoint's predicate produces, using
// HealthCheckService directly (no web host). ASP.NET Core's default status-code map then yields
// Healthy/Degraded => HTTP 200 and Unhealthy => HTTP 503; the HTTP mapping itself is framework
// behavior. The predicates below are the SAME production delegates UseFhirServer maps onto the
// endpoints, so a routing change in HealthProbePredicates cannot drift away from this coverage.
[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Web)]
public class HealthCheckEndpointTests
{
    private static readonly Func<HealthCheckRegistration, bool> CheckPredicate = HealthProbePredicates.Diagnostic(null);

    private static readonly Func<HealthCheckRegistration, bool> StartupPredicate = HealthProbePredicates.Startup;

    private static readonly Func<HealthCheckRegistration, bool> ReadyPredicate = HealthProbePredicates.Readiness;

    private static readonly Func<HealthCheckRegistration, bool> LivePredicate = HealthProbePredicates.Live;

    private static async Task<HealthStatus> EvaluateAsync(
        Func<HealthCheckRegistration, bool> predicate,
        HealthStatus startupStatus,
        HealthStatus dataStoreStatus)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHealthChecks()
            .AddCheck("StorageInitializedHealthCheck", new FixedCheck(startupStatus), tags: new[] { HealthCheckTags.ProbeStartup })
            .AddCheck(HealthCheckTags.DataStoreHealthCheckName, new FixedCheck(dataStoreStatus), tags: new[] { HealthCheckTags.ProbeReadiness })
            .AddCheck("BehaviorHealthCheck", new FixedCheck(HealthStatus.Healthy), tags: new[] { HealthCheckTags.ProbeReadiness });

        HealthCheckService service = services.BuildServiceProvider().GetRequiredService<HealthCheckService>();
        HealthReport report = await service.CheckHealthAsync(predicate, CancellationToken.None);
        return report.Status;
    }

    [Fact]
    public async Task GivenInitializingStartup_WhenEvaluatingStartup_ThenUnhealthy_MapsTo503()
    {
        HealthStatus status = await EvaluateAsync(StartupPredicate, HealthStatus.Unhealthy, HealthStatus.Healthy);
        Assert.Equal(HealthStatus.Unhealthy, status);
    }

    [Fact]
    public async Task GivenInitializedStartup_WhenEvaluatingStartup_ThenHealthy_MapsTo200()
    {
        HealthStatus status = await EvaluateAsync(StartupPredicate, HealthStatus.Healthy, HealthStatus.Healthy);
        Assert.Equal(HealthStatus.Healthy, status);
    }

    [Fact]
    public async Task GivenDegradedDataStore_WhenEvaluatingReady_ThenDegraded_MapsTo200_StaysRoutable()
    {
        HealthStatus status = await EvaluateAsync(ReadyPredicate, HealthStatus.Healthy, HealthStatus.Degraded);
        Assert.Equal(HealthStatus.Degraded, status);
    }

    [Fact]
    public async Task GivenUnhealthyDataStore_WhenEvaluatingReady_ThenUnhealthy_MapsTo503()
    {
        HealthStatus status = await EvaluateAsync(ReadyPredicate, HealthStatus.Healthy, HealthStatus.Unhealthy);
        Assert.Equal(HealthStatus.Unhealthy, status);
    }

    [Fact]
    public async Task GivenAnyState_WhenEvaluatingLive_ThenHealthy_MapsTo200()
    {
        HealthStatus status = await EvaluateAsync(LivePredicate, HealthStatus.Unhealthy, HealthStatus.Unhealthy);
        Assert.Equal(HealthStatus.Healthy, status);
    }

    [Fact]
    public async Task GivenInitializingStartup_WhenEvaluatingCheck_ThenStartupExcluded_Healthy_MapsTo200()
    {
        // /health/check excludes probe:startup, so an initializing pod with a reachable DB is Healthy => 200.
        HealthStatus status = await EvaluateAsync(CheckPredicate, HealthStatus.Unhealthy, HealthStatus.Healthy);
        Assert.Equal(HealthStatus.Healthy, status);
    }

    private sealed class FixedCheck : IHealthCheck
    {
        private readonly HealthStatus _status;

        public FixedCheck(HealthStatus status) => _status = status;

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new HealthCheckResult(_status));
    }
}
