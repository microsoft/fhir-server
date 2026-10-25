// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Integration.Features.Operations.Reindex;
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
        [InlineData(DeleteOperation.SoftDelete, SearchParameterStatus.PendingDelete, false)]
        [InlineData(DeleteOperation.HardDelete, SearchParameterStatus.PendingHardDelete, false)]
        [InlineData(DeleteOperation.SoftDelete, SearchParameterStatus.PendingDelete, true)]
        [InlineData(DeleteOperation.HardDelete, SearchParameterStatus.PendingHardDelete, true)]
        public async Task GivenForegroundDelete_WhenDispatchedAcrossAwait_ThenPendingStatusAndExistingAuthorizationArePreserved(
            DeleteOperation deleteOperation,
            SearchParameterStatus expectedStatus,
            bool hasExplicitContext)
        {
            var accessor = new FhirStorageTestRequestContextAccessor();
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

            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            IFhirRequestContext helperContext = null;
            var operations = Substitute.For<ISearchParameterOperations>();
            operations.SearchParamLastUpdated.Returns(DateTimeOffset.UtcNow);
            operations.GetAndApplySearchParameterUpdates(CancellationToken.None).Returns(_ =>
            {
                helperContext = accessor.RequestContext;
                entered.SetResult();
                return release.Task;
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
                    Assert.Same(explicitContext.AccessControlContext, context.AccessControlContext);
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

            Task<DeleteResourceResponse> dispatch = mediator.DeleteResourceAsync(key, deleteOperation);
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.False(dispatch.IsCompleted);
            }
            finally
            {
                release.SetResult(true);
            }

            Assert.Equal(key, (await dispatch).ResourceKey);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GivenStorageFixture_WhenBackgroundContextsAreAssigned_ThenRequestStateIsIsolated(bool useCosmos)
        {
            IServiceProvider fixture = useCosmos ? new CosmosDbFhirStorageTestsFixture() : new SqlServerFhirStorageTestsFixture();
            var accessor = (RequestContextAccessor<IFhirRequestContext>)fixture.GetService(typeof(RequestContextAccessor<IFhirRequestContext>));
            IFhirRequestContext foregroundContext = accessor.RequestContext;
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int readyCount = 0;
            Task<IFhirRequestContext>[] workers = Enumerable.Range(0, 2).Select(worker => Task.Run(async () =>
            {
                IFhirRequestContext context = FhirStorageTestRequestContextAccessor.CreateRequestContext();
                context.Properties["worker"] = worker;
                accessor.RequestContext = context;
                if (Interlocked.Increment(ref readyCount) == 2)
                {
                    ready.SetResult();
                }

                await release.Task;
                return accessor.RequestContext;
            })).ToArray();

            try
            {
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally
            {
                release.SetResult();
            }

            IFhirRequestContext[] contexts = await Task.WhenAll(workers);

            Assert.Same(foregroundContext, accessor.RequestContext);
            Assert.NotSame(foregroundContext, contexts[0]);
            Assert.NotSame(foregroundContext, contexts[1]);
            Assert.NotSame(contexts[0], contexts[1]);
            Assert.Equal(0, contexts[0].Properties["worker"]);
            Assert.Equal(1, contexts[1].Properties["worker"]);
            Assert.False(foregroundContext.Properties.ContainsKey("worker"));
        }

        [Fact]
        public async Task GivenInFlightCacheRefresh_WhenCancelled_ThenTaskWaitsForRefreshAndUsesIndependentContext()
        {
            var accessor = new FhirStorageTestRequestContextAccessor();
            IFhirRequestContext foregroundContext = accessor.RequestContext;
            IFhirRequestContext cacheContext = null;
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var operations = Substitute.For<ISearchParameterOperations>();
            using var cancellation = new CancellationTokenSource();
            operations.GetAndApplySearchParameterUpdates(cancellation.Token, true).Returns(_ =>
            {
                cacheContext = accessor.RequestContext;
                cacheContext.Properties["cache"] = true;
                entered.SetResult();
                return release.Task;
            });

            Task refresh = ReindexJobTests.RefreshSearchParameterCacheAsync(operations, accessor, TimeSpan.FromHours(1), cancellation.Token);
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                cancellation.Cancel();

                Assert.False(refresh.IsCompleted);
                Assert.NotSame(foregroundContext, cacheContext);
                Assert.Same(foregroundContext, accessor.RequestContext);
                Assert.False(foregroundContext.Properties.ContainsKey("cache"));
            }
            finally
            {
                cancellation.Cancel();
                release.SetResult(true);
            }

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
        }

        [Fact]
        public async Task GivenFailedCacheRefresh_WhenAwaitingTask_ThenFailureIsPropagated()
        {
            var accessor = new FhirStorageTestRequestContextAccessor();
            var operations = Substitute.For<ISearchParameterOperations>();
            var failure = new InvalidOperationException("Refresh failed.");
            operations.GetAndApplySearchParameterUpdates(CancellationToken.None, true).Returns(Task.FromException<bool>(failure));

            Task refresh = ReindexJobTests.RefreshSearchParameterCacheAsync(operations, accessor, TimeSpan.FromSeconds(1), CancellationToken.None);

            Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => refresh));
        }
    }
}
