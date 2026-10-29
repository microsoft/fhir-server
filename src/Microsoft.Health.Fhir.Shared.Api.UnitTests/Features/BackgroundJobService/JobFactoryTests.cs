// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Api.Features.BackgroundJobService;
using Microsoft.Health.Fhir.Core.Features.Scoping;
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
        IOwnedScope<IEnumerable<Func<IJob>>> discovery = CreateScope(() => job);
        IOwnedScope<IEnumerable<Func<IJob>>> execution = CreateScope(() => job);
        var factory = Substitute.For<IOwnedScopeFactory<IEnumerable<Func<IJob>>>>();
        factory.Create().Returns(discovery, execution);
        IJobFactory jobFactory = new JobFactory(factory, NullLogger<JobFactory>.Instance);

        using IJobScope scope = jobFactory.Create(CreateJobInfo());

        Assert.Same(job, scope.Job);
        discovery.Received(1).Dispose();
        execution.DidNotReceive().Dispose();
        scope.Dispose();
        execution.Received(1).Dispose();
    }

    [Fact]
    public void GivenExecutionActivationFailure_WhenCreatingJob_ThenExecutionScopeIsDisposed()
    {
        IOwnedScope<IEnumerable<Func<IJob>>> discovery = CreateScope(() => new TestJob());
        IOwnedScope<IEnumerable<Func<IJob>>> execution = CreateScope(() => throw new InvalidOperationException("Activation failed."));
        var factory = Substitute.For<IOwnedScopeFactory<IEnumerable<Func<IJob>>>>();
        factory.Create().Returns(discovery, execution);
        IJobFactory jobFactory = new JobFactory(factory, NullLogger<JobFactory>.Instance);

        Assert.Throws<InvalidOperationException>(() => jobFactory.Create(CreateJobInfo()));

        execution.Received(1).Dispose();
    }

    [Fact]
    public void GivenDiscoveryActivationFailure_WhenConstructingFactory_ThenDiscoveryScopeIsDisposed()
    {
        IOwnedScope<IEnumerable<Func<IJob>>> discovery = CreateScope(() => throw new InvalidOperationException("Activation failed."));
        var factory = CreateFactory(discovery);

        Assert.Throws<InvalidOperationException>(() => new JobFactory(factory, NullLogger<JobFactory>.Instance));

        discovery.Received(1).Dispose();
    }

    [Fact]
    public void GivenUnknownJob_WhenCreatingJob_ThenNoExecutionScopeIsCreated()
    {
        IOwnedScope<IEnumerable<Func<IJob>>> discovery = CreateScope(() => new TestJob());
        var factory = CreateFactory(discovery);
        IJobFactory jobFactory = new JobFactory(factory, NullLogger<JobFactory>.Instance);

        Assert.Throws<NotSupportedException>(() => jobFactory.Create(new JobInfo { Definition = "{\"typeId\":-1}" }));

        factory.Received(1).Create();
    }

    private static IOwnedScope<IEnumerable<Func<IJob>>> CreateScope(Func<IJob> createJob)
    {
        var scope = Substitute.For<IOwnedScope<IEnumerable<Func<IJob>>>>();
        scope.Value.Returns(new[] { createJob });
        return scope;
    }

    private static IOwnedScopeFactory<IEnumerable<Func<IJob>>> CreateFactory(IOwnedScope<IEnumerable<Func<IJob>>> scope)
    {
        var factory = Substitute.For<IOwnedScopeFactory<IEnumerable<Func<IJob>>>>();
        factory.Create().Returns(scope);
        return factory;
    }

    private static JobInfo CreateJobInfo() => new JobInfo { Definition = "{\"typeId\":999}" };

    [JobTypeId(999)]
    private sealed class TestJob : IJob
    {
        public Task<string> ExecuteAsync(JobInfo jobInfo, CancellationToken cancellationToken) => Task.FromResult("Done");
    }
}
