// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Api.Features.BackgroundJobService;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Scoping;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.BackgroundJobService;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Operations)]
public class JobFactoryTests
{
    [Fact]
    public void GivenKnownJob_WhenCreated_ThenCallerOwnsExecutionScope()
    {
        IJob job = new TestJob();
        IScoped<IEnumerable<Func<IJob>>> discovery = CreateScope(() => job);
        IScoped<IEnumerable<Func<IJob>>> execution = CreateScope(() => job);
        var factory = Substitute.For<IScopeProvider<IEnumerable<Func<IJob>>>>();
        factory.Invoke().Returns(discovery, execution);
        IJobFactory jobFactory = new JobFactory(factory, NullLogger<JobFactory>.Instance);

        using IScoped<IJob> scope = jobFactory.Create(CreateJobInfo());

        Assert.Same(job, scope.Value);
        discovery.Received(1).Dispose();
        execution.DidNotReceive().Dispose();
        scope.Dispose();
        execution.Received(1).Dispose();
    }

    [Fact]
    public void GivenExecutionActivationFailure_WhenCreatingJob_ThenExecutionScopeIsDisposed()
    {
        IScoped<IEnumerable<Func<IJob>>> discovery = CreateScope(() => new TestJob());
        IScoped<IEnumerable<Func<IJob>>> execution = CreateScope(() => throw new InvalidOperationException("Activation failed."));
        var factory = Substitute.For<IScopeProvider<IEnumerable<Func<IJob>>>>();
        factory.Invoke().Returns(discovery, execution);
        IJobFactory jobFactory = new JobFactory(factory, NullLogger<JobFactory>.Instance);

        Assert.Throws<InvalidOperationException>(() => jobFactory.Create(CreateJobInfo()));

        execution.Received(1).Dispose();
    }

    [Fact]
    public void GivenDiscoveryActivationFailure_WhenConstructingFactory_ThenDiscoveryScopeIsDisposed()
    {
        IScoped<IEnumerable<Func<IJob>>> discovery = CreateScope(() => throw new InvalidOperationException("Activation failed."));
        var factory = CreateFactory(discovery);

        Assert.Throws<InvalidOperationException>(() => new JobFactory(factory, NullLogger<JobFactory>.Instance));

        discovery.Received(1).Dispose();
    }

    [Fact]
    public void GivenUnknownJob_WhenCreatingJob_ThenNoExecutionScopeIsCreated()
    {
        IScoped<IEnumerable<Func<IJob>>> discovery = CreateScope(() => new TestJob());
        var factory = CreateFactory(discovery);
        IJobFactory jobFactory = new JobFactory(factory, NullLogger<JobFactory>.Instance);

        Assert.Throws<NotSupportedException>(() => jobFactory.Create(new JobInfo { Definition = "{\"typeId\":-1}" }));

        factory.Received(1).Invoke();
    }

    [Fact]
    public async Task GivenTwoJobs_WhenFirstExecutionScopeIsDisposed_ThenSecondJobRemainsUsable()
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopedJobDependency>();
        services.AddScoped<Func<IJob>>(provider => () => new ScopeAwareJob(provider.GetRequiredService<ScopedJobDependency>()));
        using ServiceProvider serviceProvider = services.BuildServiceProvider();

        IJobFactory jobFactory = new JobFactory(
            new ScopeProvider<IEnumerable<Func<IJob>>>(serviceProvider),
            NullLogger<JobFactory>.Instance);

        using IScoped<IJob> firstScope = jobFactory.Create(CreateJobInfo());
        using IScoped<IJob> secondScope = jobFactory.Create(CreateJobInfo());
        var firstJob = Assert.IsType<ScopeAwareJob>(firstScope.Value);
        var secondJob = Assert.IsType<ScopeAwareJob>(secondScope.Value);

        firstScope.Dispose();

        Assert.True(firstJob.Dependency.IsDisposed);
        Assert.Equal("Done", await secondJob.ExecuteAsync(CreateJobInfo(), CancellationToken.None));
    }

    private static IScoped<IEnumerable<Func<IJob>>> CreateScope(Func<IJob> createJob)
    {
        var scope = Substitute.For<IScoped<IEnumerable<Func<IJob>>>>();
        scope.Value.Returns(new[] { createJob });
        return scope;
    }

    private static IScopeProvider<IEnumerable<Func<IJob>>> CreateFactory(IScoped<IEnumerable<Func<IJob>>> scope)
    {
        var factory = Substitute.For<IScopeProvider<IEnumerable<Func<IJob>>>>();
        factory.Invoke().Returns(scope);
        return factory;
    }

    private static JobInfo CreateJobInfo() => new JobInfo { Definition = "{\"typeId\":999}" };

    [JobTypeId(999)]
    private sealed class TestJob : IJob
    {
        public Task<string> ExecuteAsync(JobInfo jobInfo, CancellationToken cancellationToken) => Task.FromResult("Done");
    }

    [JobTypeId(999)]
    private sealed class ScopeAwareJob : IJob
    {
        public ScopeAwareJob(ScopedJobDependency dependency)
        {
            Dependency = dependency;
        }

        public ScopedJobDependency Dependency { get; }

        public Task<string> ExecuteAsync(JobInfo jobInfo, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(Dependency.IsDisposed, Dependency);
            return Task.FromResult("Done");
        }
    }

    private sealed class ScopedJobDependency : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
