// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Caching;
using Microsoft.Health.Fhir.Core.Features.Partitions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Caching
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Caching)]
    public class CacheInitializerTests
    {
        private readonly IPartitionManager _partitionManager = Substitute.For<IPartitionManager>();

        [Fact]
        public async Task GivenPartitioningIsEnabled_WhenStarted_ThenEveryCacheIsLoadedForEveryPartition()
        {
            _partitionManager.IsEnabled.Returns(true);
            IPartitionInfo[] partitions = [CreatePartition("a"), CreatePartition("b")];
            _partitionManager.GetPartitions().Returns(partitions);
            using ServiceProvider provider = BuildProvider(out FirstCache first, out SecondCache second);

            await StartAsync(provider);

            Assert.Equal(["a", "b"], first.Loads.Select(l => l.PartitionId));
            Assert.Equal(["a", "b"], second.Loads.Select(l => l.PartitionId));
            Assert.All(first.Loads, load => Assert.True(load.Async));
        }

        [Fact]
        public async Task GivenPartitioningIsDisabled_WhenStarted_ThenEveryCacheIsLoadedOnceWithoutAPartition()
        {
            _partitionManager.IsEnabled.Returns(false);
            using ServiceProvider provider = BuildProvider(out FirstCache first, out SecondCache second);

            await StartAsync(provider);

            Assert.Equal([(null, true)], first.Loads);
            Assert.Equal([(null, true)], second.Loads);
        }

        [Fact]
        public async Task GivenACacheFailsToLoad_WhenStarted_ThenStartupContinuesAndTheOtherCachesLoad()
        {
            _partitionManager.IsEnabled.Returns(false);
            using ServiceProvider provider = BuildProvider(out FirstCache first, out SecondCache second, firstFails: true);

            await StartAsync(provider);

            Assert.Single(first.Loads);
            Assert.Single(second.Loads);
        }

        [Fact]
        public void GivenACacheIsRegisteredTwice_WhenRegistered_ThenItIsPreloadedOnce()
        {
            var services = new ServiceCollection();
            services.AddPartitionCache<FirstCache, int>();
            services.AddPartitionCache<FirstCache, int>();

            Assert.Single(services, d => d.ServiceType == typeof(PartitionCachePreload));
            Assert.Single(services, d => d.ServiceType == typeof(IHostedService));
        }

        private ServiceProvider BuildProvider(out FirstCache first, out SecondCache second, bool firstFails = false)
        {
            first = new FirstCache(firstFails);
            second = new SecondCache();

            var services = new ServiceCollection();
            services.AddSingleton(_partitionManager);
            services.AddSingleton<ILogger<CacheInitializer>>(NullLogger<CacheInitializer>.Instance);
            services.AddSingleton(first);
            services.AddSingleton(second);
            services.AddPartitionCache<FirstCache, int>();
            services.AddPartitionCache<SecondCache, string>();

            return services.BuildServiceProvider();
        }

        private static Task StartAsync(IServiceProvider provider) =>
            provider.GetServices<IHostedService>().OfType<CacheInitializer>().Single().StartAsync(CancellationToken.None);

        private static IPartitionInfo CreatePartition(string id)
        {
            IPartitionInfo partition = Substitute.For<IPartitionInfo>();
            partition.Id.Returns(id);
            return partition;
        }

        private sealed class FirstCache : TestPartitionCache<int>
        {
            public FirstCache(bool fails)
                : base((_, _, _) => fails ? throw new InvalidOperationException("load failed") : new ValueTask<int>(1))
            {
            }
        }

        private sealed class SecondCache : TestPartitionCache<string>
        {
            public SecondCache()
                : base((_, _, _) => new ValueTask<string>("value"))
            {
            }
        }
    }
}
