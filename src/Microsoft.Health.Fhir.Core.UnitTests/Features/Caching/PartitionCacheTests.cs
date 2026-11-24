// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Health.Fhir.Core.Features.Partitions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Caching
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Caching)]
    public class PartitionCacheTests
    {
        private static readonly IPartitionInfo PartitionA = CreatePartition("a");
        private static readonly IPartitionInfo PartitionB = CreatePartition("b");

        [Fact]
        public async Task GivenTwoPartitions_WhenRead_ThenEachPartitionGetsItsOwnValue()
        {
            var cache = new TestPartitionCache<string>((partition, _, _) => new ValueTask<string>($"value-{partition?.Id ?? "default"}"));

            Assert.Equal("value-a", await cache.GetAsync(PartitionA));
            Assert.Equal("value-b", await cache.GetAsync(PartitionB));
            Assert.Equal("value-default", await cache.GetAsync(null));
            Assert.Equal("value-a", cache.Get(PartitionA));

            Assert.Equal(3, cache.Loads.Count);
        }

        [Fact]
        public void GivenAMissingValue_WhenReadSynchronously_ThenTheLoaderRunsWithAsyncFalse()
        {
            var cache = new TestPartitionCache<int>((_, _, _) => new ValueTask<int>(1));

            cache.Get(PartitionA);

            Assert.Equal([("a", false)], cache.Loads);
        }

        [Fact]
        public async Task GivenALoaderThatAwaitsWhenAsyncIsFalse_WhenReadSynchronously_ThenItThrowsInsteadOfBlocking()
        {
            var release = new TaskCompletionSource<int>();
            var cache = new TestPartitionCache<int>(async (_, _, _) => await release.Task);

            Assert.Throws<InvalidOperationException>(() => cache.Get(PartitionA));

            release.SetResult(1);
            await Task.Yield();
        }

        [Fact]
        public async Task GivenAnExpiry_WhenItElapses_ThenTheValueIsReloaded()
        {
            var time = new FakeTimeProvider();
            int loads = 0;
            var cache = new TestPartitionCache<int>((_, _, _) => new ValueTask<int>(++loads), TimeSpan.FromSeconds(60), time);

            Assert.Equal(1, await cache.GetAsync(PartitionA));
            time.Advance(TimeSpan.FromSeconds(59));
            Assert.Equal(1, await cache.GetAsync(PartitionA));
            time.Advance(TimeSpan.FromSeconds(1));
            Assert.Equal(2, await cache.GetAsync(PartitionA));
        }

        [Fact]
        public async Task GivenAPreviousValue_WhenARefreshFails_ThenThePreviousValueIsKeptUntilTheNextExpiry()
        {
            var time = new FakeTimeProvider();
            int calls = 0;
            var cache = new TestPartitionCache<int>(
                (_, _, _) => ++calls == 2 ? throw new InvalidOperationException("refresh failed") : new ValueTask<int>(calls),
                TimeSpan.FromSeconds(60),
                time);

            Assert.Equal(1, await cache.GetAsync(PartitionA));
            time.Advance(TimeSpan.FromSeconds(60));

            Assert.Equal(1, await cache.GetAsync(PartitionA));
            Assert.Equal(1, await cache.GetAsync(PartitionA));
            Assert.Equal(2, cache.Loads.Count);

            time.Advance(TimeSpan.FromSeconds(60));
            Assert.Equal(3, await cache.GetAsync(PartitionA));
        }

        [Fact]
        public async Task GivenNoPreviousValue_WhenTheLoadFails_ThenTheErrorPropagatesAndNothingIsCached()
        {
            int calls = 0;
            var cache = new TestPartitionCache<int>((_, _, _) => ++calls == 1 ? throw new InvalidOperationException("load failed") : new ValueTask<int>(calls));

            await Assert.ThrowsAsync<InvalidOperationException>(async () => await cache.GetAsync(PartitionA));
            Assert.Equal(2, await cache.GetAsync(PartitionA));
        }

        [Fact]
        public async Task GivenConcurrentReaders_WhenTheValueIsMissing_ThenItLoadsOnce()
        {
            var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cache = new TestPartitionCache<int>(async (_, _, _) => await release.Task);

            Task<int>[] readers = Enumerable.Range(0, 10).Select(_ => cache.GetAsync(PartitionA).AsTask()).ToArray();
            release.SetResult(7);

            Assert.All(await Task.WhenAll(readers), value => Assert.Equal(7, value));
            Assert.Single(cache.Loads);
        }

        [Fact]
        public async Task GivenOnePartitionIsLoading_WhenAnotherPartitionIsRead_ThenItIsNotBlocked()
        {
            var releaseA = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cache = new TestPartitionCache<string>((partition, _, _) => partition.Id == "a" ? new ValueTask<string>(releaseA.Task) : new ValueTask<string>("b"));

            Task<string> readA = cache.GetAsync(PartitionA).AsTask();

            Assert.Equal("b", await cache.GetAsync(PartitionB));
            Assert.False(readA.IsCompleted);

            releaseA.SetResult("a");
            Assert.Equal("a", await readA);
        }

        private static IPartitionInfo CreatePartition(string id)
        {
            IPartitionInfo partition = Substitute.For<IPartitionInfo>();
            partition.Id.Returns(id);
            return partition;
        }
    }
}
