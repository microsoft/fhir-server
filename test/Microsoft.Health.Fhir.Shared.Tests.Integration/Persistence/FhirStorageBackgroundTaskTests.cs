// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.Integration.Features.Operations.Reindex;
using Microsoft.Health.JobManagement;
using Microsoft.Health.JobManagement.UnitTests;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class FhirStorageBackgroundTaskTests
    {
        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task GivenForegroundRequest_WhenNestedWorkAwaits_ThenStateAndExistingAuthorizationArePreserved(bool useCosmos, bool hasExplicitContext)
        {
            using var fixture = new FhirStorageTestsFixture(useCosmos ? DataStore.CosmosDb : DataStore.SqlServer);
            var accessor = fixture.FhirRequestContextAccessor;
            IFhirRequestContext explicitContext = null;
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("scope", "patient/*.read") }, "SMART"));
            if (hasExplicitContext)
            {
                explicitContext = FhirStorageTestRequestContextAccessor.CreateRequestContext();
                explicitContext.Principal = principal;
                explicitContext.AccessControlContext.ApplyFineGrainedAccessControl = true;
                explicitContext.AccessControlContext.ClinicalScopes.Add("patient/*.read");
                accessor.RequestContext = explicitContext;
            }

            var key = new ResourceKey("SearchParameter", "foreground");
            var pending = new ResourceSearchParameterStatus { Uri = new Uri("http://example.org/SearchParameter/foreground"), Status = SearchParameterStatus.PendingDelete };
            async Task QueuePendingStatusAsync()
            {
                await Task.Yield();
                accessor.RequestContext.Properties[SearchParameterRequestContextPropertyNames.PendingStatus] = pending;
            }

            async Task<DeleteResourceResponse> HandleAsync()
            {
                await QueuePendingStatusAsync();
                IFhirRequestContext context = accessor.RequestContext;
                Assert.Same(pending, context.Properties[SearchParameterRequestContextPropertyNames.PendingStatus]);
                if (hasExplicitContext)
                {
                    Assert.Same(explicitContext, context);
                    Assert.Same(principal, context.Principal);
                    Assert.Equal("patient/*.read", context.Principal.FindFirst("scope")?.Value);
                    Assert.True(context.AccessControlContext.ApplyFineGrainedAccessControl);
                    Assert.Equal("patient/*.read", Assert.Single(context.AccessControlContext.ClinicalScopes));
                }

                return new DeleteResourceResponse(key);
            }

            var request = new DeleteResourceRequest(key, DeleteOperation.SoftDelete);
            var contextBehavior = new FhirStorageRequestContextBehavior<DeleteResourceRequest, DeleteResourceResponse>(accessor);
            DeleteResourceResponse response = await contextBehavior.HandleAsync(request, HandleAsync, CancellationToken.None);
            Assert.Equal(key, response.ResourceKey);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public async Task GivenReindexHost_WhenJobsAndRefreshRun_ThenContextsAreIsolatedAndShutdownJoinsThem(bool useCosmos, bool failRefresh)
        {
            using var fixture = new FhirStorageTestsFixture(useCosmos ? DataStore.CosmosDb : DataStore.SqlServer);
            var accessor = fixture.FhirRequestContextAccessor;
            IFhirRequestContext foregroundContext = accessor.RequestContext;
            var workerContexts = new ConcurrentBag<IFhirRequestContext>();
            var jobContexts = new ConcurrentBag<IFhirRequestContext>();
            var jobsStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var jobsCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var heartbeatContext = new TaskCompletionSource<IFhirRequestContext>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseJobs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task SetJobStateAsync()
            {
                await Task.Yield();
                accessor.RequestContext.Properties["job-state"] = true;
            }

            var job = Substitute.For<IJob>();
            job.ExecuteAsync(Arg.Any<JobInfo>(), Arg.Any<CancellationToken>()).Returns(async _ =>
            {
                await SetJobStateAsync();
                jobContexts.Add(accessor.RequestContext);
                if (jobContexts.Count == 2)
                {
                    jobsStarted.TrySetResult();
                }

                return await releaseJobs.Task;
            });
            int completed = 0;
            var queue = new TestQueueClient
            {
                DequeueFaultAction = () => workerContexts.Add(accessor.RequestContext),
                HeartbeatFaultAction = () => heartbeatContext.TrySetResult(accessor.RequestContext),
                CompleteFaultAction = () =>
                {
                    if (Interlocked.Increment(ref completed) == 2)
                    {
                        jobsCompleted.TrySetResult();
                    }
                },
            };
            await queue.EnqueueAsync((byte)QueueType.Reindex, new[] { "{\"typeId\":0,\"id\":1}", "{\"typeId\":0,\"id\":2}" }, null, false, CancellationToken.None);
            var cacheContext = new TaskCompletionSource<IFhirRequestContext>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseRefresh = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var operations = Substitute.For<ISearchParameterOperations>();
            operations.GetAndApplySearchParameterUpdates(Arg.Any<CancellationToken>(), true).Returns(_ =>
            {
                cacheContext.TrySetResult(accessor.RequestContext);
                return releaseRefresh.Task;
            });
            var runner = new ReindexJobTests(fixture, Substitute.For<ITestOutputHelper>());
            runner.InitializeJobHosting(queue, job);
            runner.StartCacheUpdateTask(operations, TimeSpan.FromHours(1));
            var failure = new InvalidOperationException("Refresh failed.");
            Task shutdown = null;
            try
            {
                await jobsStarted.Task.WaitAsync(TimeSpan.FromSeconds(15));
                IFhirRequestContext[] contexts =
                [
                    foregroundContext,
                    .. workerContexts,
                    .. jobContexts,
                    await heartbeatContext.Task.WaitAsync(TimeSpan.FromSeconds(15)),
                    await cacheContext.Task.WaitAsync(TimeSpan.FromSeconds(15)),
                ];
                Assert.Equal(7, contexts.Length);
                Assert.Equal(7, contexts.Select(context => context.Properties).Distinct(ReferenceEqualityComparer.Instance).Count());
                Assert.All(jobContexts, context => Assert.True(context.Properties.TryGetValue("job-state", out object value) && Equals(value, true)));
                Assert.Same(foregroundContext, accessor.RequestContext);
                shutdown = runner.DisposeAsync().AsTask();
                Assert.False(shutdown.IsCompleted);
                if (failRefresh)
                {
                    releaseRefresh.SetException(failure);
                }
                else
                {
                    releaseJobs.SetResult("Success");
                    await jobsCompleted.Task.WaitAsync(TimeSpan.FromSeconds(15));
                }

                Assert.False(shutdown.IsCompleted);
            }
            finally
            {
                shutdown ??= runner.DisposeAsync().AsTask();
                releaseJobs.TrySetResult("Success");
                if (failRefresh)
                {
                    releaseRefresh.TrySetException(failure);
                }
                else
                {
                    releaseRefresh.TrySetResult(true);
                }

                if (failRefresh)
                {
                    Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => shutdown.WaitAsync(TimeSpan.FromSeconds(15))));
                }
                else
                {
                    await shutdown.WaitAsync(TimeSpan.FromSeconds(15));
                }
            }
        }
    }
}
