// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Features.Health;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Registration;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Web)]
public class HealthCheckRegistrationTests
{
    private static bool Readiness(HealthCheckRegistration reg) =>
        reg.Tags.Contains(HealthCheckTags.DataStoreSqlServer) || reg.Tags.Contains(HealthCheckTags.ProbeReadiness);

    [Fact]
    public void GivenSqlDataStoreTag_WhenResolvingReadiness_ThenExactlyOneDataStoreCheck()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks()
            .AddCheck<StubCheck>(HealthCheckTags.DataStoreHealthCheckName, tags: new[] { HealthCheckTags.DataStoreSqlServer })
            .AddCheck<StubCheck>("BehaviorHealthCheck", tags: new[] { HealthCheckTags.ProbeReadiness })
            .AddCheck<StubCheck>("StorageInitializedHealthCheck", tags: new[] { HealthCheckTags.ProbeStartup });

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;

        int dataStoreCount = options.Registrations.Count(r => Readiness(r) && r.Name == HealthCheckTags.DataStoreHealthCheckName);
        Assert.Equal(1, dataStoreCount);
        Assert.Equal(1, options.Registrations.Count(r => r.Tags.Contains(HealthCheckTags.ProbeStartup)));
    }

    [Fact]
    public void GivenCosmosReadinessTag_WhenResolvingReadiness_ThenExactlyOneDataStoreCheck()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks()
            .AddCheck<StubCheck>(HealthCheckTags.DataStoreHealthCheckName, tags: new[] { HealthCheckTags.ProbeReadiness })
            .AddCheck<StubCheck>("BehaviorHealthCheck", tags: new[] { HealthCheckTags.ProbeReadiness })
            .AddCheck<StubCheck>("StorageInitializedHealthCheck", tags: new[] { HealthCheckTags.ProbeStartup });

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;

        Assert.Equal(1, options.Registrations.Count(r => Readiness(r) && r.Name == HealthCheckTags.DataStoreHealthCheckName));
    }

    [Fact]
    public void GivenMissingDataStoreTag_WhenResolvingReadiness_ThenZeroDataStoreChecks()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks()
            .AddCheck<StubCheck>(HealthCheckTags.DataStoreHealthCheckName) // no tag => package-skew simulation
            .AddCheck<StubCheck>("StorageInitializedHealthCheck", tags: new[] { HealthCheckTags.ProbeStartup });

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;

        Assert.Equal(0, options.Registrations.Count(r => Readiness(r) && r.Name == HealthCheckTags.DataStoreHealthCheckName));
    }

    private sealed class StubCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(HealthCheckResult.Healthy());
    }
}
