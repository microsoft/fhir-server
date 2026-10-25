// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
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
