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
using Medino;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.Integration.Features.Operations.Reindex;
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
        [InlineData(DeleteOperation.SoftDelete, SearchParameterStatus.PendingDelete, false, false)]
        [InlineData(DeleteOperation.HardDelete, SearchParameterStatus.PendingHardDelete, false, false)]
        [InlineData(DeleteOperation.SoftDelete, SearchParameterStatus.PendingDelete, true, false)]
        [InlineData(DeleteOperation.HardDelete, SearchParameterStatus.PendingHardDelete, true, false)]
        [InlineData(DeleteOperation.SoftDelete, SearchParameterStatus.PendingDelete, false, true)]
        [InlineData(DeleteOperation.HardDelete, SearchParameterStatus.PendingHardDelete, false, true)]
        [InlineData(DeleteOperation.SoftDelete, SearchParameterStatus.PendingDelete, true, true)]
        [InlineData(DeleteOperation.HardDelete, SearchParameterStatus.PendingHardDelete, true, true)]
        public async Task GivenForegroundDelete_WhenDispatchedAcrossAwait_ThenPendingStatusAndExistingAuthorizationArePreserved(
            DeleteOperation deleteOperation,
            SearchParameterStatus expectedStatus,
            bool hasExplicitContext,
            bool useCosmos)
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

            AccessControlContext accessControlContext = explicitContext?.AccessControlContext;
            var key = new ResourceKey("SearchParameter", "foreground");
            var uri = new Uri("http://example.org/SearchParameter/foreground");
            var rawResource = new RawResource(
                "{\"resourceType\":\"SearchParameter\",\"id\":\"foreground\",\"url\":\"http://example.org/SearchParameter/foreground\"}",
                FhirResourceFormat.Json,
                isMetaSet: false);
            var resource = new ResourceWrapper(key.Id, "1", key.ResourceType, rawResource, null, DateTimeOffset.UtcNow, false, null, null, null);
            var dataStore = Substitute.For<IFhirDataStore>();
            dataStore.GetAsync(key, CancellationToken.None).Returns(async _ =>
            {
                await Task.Yield();
                return resource;
            });

            IFhirRequestContext helperContext = null;
            var operations = Substitute.For<ISearchParameterOperations>();
            operations.SearchParamLastUpdated.Returns(DateTimeOffset.UtcNow);
            operations.GetAndApplySearchParameterUpdates(CancellationToken.None).Returns(async _ =>
            {
                helperContext = accessor.RequestContext;
                await Task.Yield();
                return true;
            });

            var handler = Substitute.For<IRequestHandler<DeleteResourceRequest, DeleteResourceResponse>>();
            handler.HandleAsync(Arg.Any<DeleteResourceRequest>(), CancellationToken.None).Returns(async _ =>
            {
                await Task.Yield();
                IFhirRequestContext context = accessor.RequestContext;
                Assert.True(context.Properties.TryGetValue(SearchParameterRequestContextPropertyNames.PendingStatus, out object pending), "Awaited delete handler must see pending search parameter status.");
                var status = Assert.IsType<ResourceSearchParameterStatus>(pending);
                Assert.Equal(uri, status.Uri);
                Assert.Equal(expectedStatus, status.Status);
                Assert.Same(helperContext, context);
                if (hasExplicitContext)
                {
                    Assert.Same(explicitContext, context);
                    Assert.Same(principal, context.Principal);
                    Assert.True(context.Principal.Identity.IsAuthenticated);
                    Assert.Equal("patient/*.read", context.Principal.FindFirst("scope")?.Value);
                    Assert.Same(accessControlContext, context.AccessControlContext);
                    Assert.True(context.AccessControlContext.ApplyFineGrainedAccessControl);
                    Assert.Equal("patient/*.read", Assert.Single(context.AccessControlContext.ClinicalScopes));
                }

                return new DeleteResourceResponse(key);
            });

            var collection = new ServiceCollection();
            collection.AddSingleton<RequestContextAccessor<IFhirRequestContext>>(accessor);
            collection.AddTransient(typeof(IPipelineBehavior<,>), typeof(FhirStorageRequestContextBehavior<,>));
            collection.AddSingleton<IPipelineBehavior<DeleteResourceRequest, DeleteResourceResponse>>(
                new DeleteSearchParameterBehavior<DeleteResourceRequest, DeleteResourceResponse>(
                    operations,
                    dataStore,
                    Substitute.For<ISearchParameterDefinitionManager>(),
                    Substitute.For<ISearchParameterStatusManager>(),
                    accessor,
                    ModelInfoProvider.Instance));
            collection.AddSingleton(handler);
            using ServiceProvider services = collection.BuildServiceProvider();
            var mediator = new Mediator(services);

            Assert.Equal(key, (await mediator.DeleteResourceAsync(key, deleteOperation)).ResourceKey);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public async Task GivenReindexHostWithStubJobs_WhenDisposing_ThenContextsAreIsolatedAndRefreshIsJoined(bool useCosmos, bool failRefresh)
        {
            using var fixture = new FhirStorageTestsFixture(useCosmos ? DataStore.CosmosDb : DataStore.SqlServer);
            var accessor = fixture.FhirRequestContextAccessor;
            IFhirRequestContext foregroundContext = accessor.RequestContext;
            var hostContext = new TaskCompletionSource<IFhirRequestContext>(TaskCreationOptions.RunContinuationsAsynchronously);
            var jobContexts = new ConcurrentBag<IFhirRequestContext>();
            var jobsCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var queue = new TestQueueClient
            {
                DequeueFaultAction = () => hostContext.TrySetResult(accessor.RequestContext),
                CompleteFaultAction = () =>
                {
                    jobContexts.Add(accessor.RequestContext);
                    if (jobContexts.Count == 2)
                    {
                        jobsCompleted.TrySetResult();
                    }
                },
            };
            await queue.EnqueueAsync((byte)QueueType.Reindex, new[] { "{\"typeId\":0,\"id\":1}", "{\"typeId\":0,\"id\":2}" }, null, false, CancellationToken.None);
            var cacheContext = new TaskCompletionSource<IFhirRequestContext>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var operations = Substitute.For<ISearchParameterOperations>();
            operations.GetAndApplySearchParameterUpdates(Arg.Any<CancellationToken>(), true).Returns(_ =>
            {
                cacheContext.TrySetResult(accessor.RequestContext);
                return release.Task;
            });
            var runner = new ReindexJobTests(fixture, Substitute.For<ITestOutputHelper>());
            runner.InitializeJobHosting(queue);
            runner.StartCacheUpdateTask(operations, TimeSpan.FromHours(1));
            var failure = new InvalidOperationException("Refresh failed.");
            Task shutdown = null;
            try
            {
                await jobsCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10));
                IFhirRequestContext[] contexts =
                [
                    foregroundContext,
                    await hostContext.Task.WaitAsync(TimeSpan.FromSeconds(10)),
                    await cacheContext.Task.WaitAsync(TimeSpan.FromSeconds(10)),
                    .. jobContexts,
                ];
                Assert.Equal(5, contexts.Length);
                Assert.Equal(5, contexts.Select(context => context.Properties).Distinct(ReferenceEqualityComparer.Instance).Count());
                Assert.Same(foregroundContext, accessor.RequestContext);
                shutdown = runner.DisposeAsync().AsTask();
                Assert.False(shutdown.IsCompleted);
            }
            finally
            {
                shutdown ??= runner.DisposeAsync().AsTask();
                if (failRefresh)
                {
                    release.SetException(failure);
                }
                else
                {
                    release.SetResult(true);
                }

                if (failRefresh)
                {
                    Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => shutdown.WaitAsync(TimeSpan.FromSeconds(10))));
                }
                else
                {
                    await shutdown.WaitAsync(TimeSpan.FromSeconds(10));
                }
            }
        }
    }
}
