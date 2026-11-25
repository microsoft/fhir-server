// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Caching;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Partitions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Caching
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Caching)]
    public sealed class CacheInitializerTests : IDisposable
    {
        private readonly IPartitionManager _partitionManager = Substitute.For<IPartitionManager>();
        private readonly CancellationTokenSource _applicationStarted = new();
        private readonly IHostApplicationLifetime _lifetime = Substitute.For<IHostApplicationLifetime>();
        private readonly FhirRequestContextAccessor _requestContextAccessor = new();

        public CacheInitializerTests()
        {
            _lifetime.ApplicationStarted.Returns(_applicationStarted.Token);
        }

        public void Dispose() => _applicationStarted.Dispose();

        [Fact]
        public async Task GivenHostedServicesStarted_WhenTheAppHasNotStartedYet_ThenNothingIsLoaded()
        {
            _partitionManager.GetPartitions().Returns(Array.Empty<IPartitionInfo>());
            using ServiceProvider provider = BuildProvider(out FirstCache first, out _, out AppCache app);
            CacheInitializer initializer = GetInitializer(provider);

            await initializer.StartAsync(CancellationToken.None);

            Assert.Empty(first.Loads);
            Assert.Equal(0, app.Loads);
        }

        [Fact]
        public async Task GivenPartitioningIsEnabled_WhenStarted_ThenEveryCacheIsLoadedForEveryPartition()
        {
            IPartitionInfo[] partitions = [CreatePartition("a"), CreatePartition("b")];
            _partitionManager.GetPartitions().Returns(partitions);
            using ServiceProvider provider = BuildProvider(out FirstCache first, out SecondCache second, out AppCache app);

            await StartAsync(provider);

            Assert.Equal(["a", "b"], first.Loads.Select(l => l.PartitionId));
            Assert.Equal(["a", "b"], second.Loads.Select(l => l.PartitionId));
            Assert.All(first.Loads, load => Assert.True(load.Async));
            Assert.Equal(1, app.Loads);
        }

        [Fact]
        public async Task GivenPartitions_WhenStarted_ThenEachPartitionIsLoadedUnderABackgroundRequestContextThatDoesNotLeak()
        {
            IPartitionInfo[] partitions = [CreatePartition("a"), CreatePartition("b")];
            _partitionManager.GetPartitions().Returns(partitions);
            using ServiceProvider provider = BuildProvider(out _, out SecondCache second, out _);

            await StartAsync(provider);

            Assert.Equal(["a", "b"], second.Contexts.Select(c => c.Partition.Id));
            Assert.All(second.Contexts, context => Assert.True(context.IsBackgroundTask));
            Assert.Null(_requestContextAccessor.RequestContext);
        }

        [Fact]
        public async Task GivenPartitioningIsDisabled_WhenStarted_ThenEveryCacheIsLoadedOnceWithoutAPartition()
        {
            _partitionManager.GetPartitions().Returns(Array.Empty<IPartitionInfo>());
            using ServiceProvider provider = BuildProvider(out FirstCache first, out SecondCache second, out AppCache app);

            await StartAsync(provider);

            Assert.Equal([(null, true)], first.Loads);
            Assert.Equal([(null, true)], second.Loads);
            Assert.Equal(1, app.Loads);
        }

        [Fact]
        public async Task GivenACacheFailsToLoad_WhenStarted_ThenStartupContinuesAndTheOtherCachesLoad()
        {
            _partitionManager.GetPartitions().Returns(Array.Empty<IPartitionInfo>());
            using ServiceProvider provider = BuildProvider(out FirstCache first, out SecondCache second, out _, firstFails: true);

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
            services.AddGlobalCache<AppCache, Guid>();
            services.AddGlobalCache<AppCache, Guid>();

            Assert.Equal(2, services.Count(d => d.ServiceType == typeof(CachePreload)));
            Assert.Single(services, d => d.ServiceType == typeof(IHostedService));
        }

        private ServiceProvider BuildProvider(out FirstCache first, out SecondCache second, out AppCache app, bool firstFails = false)
        {
            first = new FirstCache(firstFails);
            second = new SecondCache(() => _requestContextAccessor.RequestContext);
            app = new AppCache();

            var services = new ServiceCollection();
            services.AddSingleton(_partitionManager);
            services.AddSingleton(_lifetime);
            services.AddSingleton<RequestContextAccessor<IFhirRequestContext>>(_requestContextAccessor);
            services.AddSingleton<ILogger<CacheInitializer>>(NullLogger<CacheInitializer>.Instance);
            services.AddSingleton(first);
            services.AddSingleton(second);
            services.AddSingleton(app);
            services.AddPartitionCache<FirstCache, int>();
            services.AddPartitionCache<SecondCache, string>();
            services.AddGlobalCache<AppCache, Guid>();

            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }

        private static CacheInitializer GetInitializer(IServiceProvider provider) =>
            provider.GetServices<IHostedService>().OfType<CacheInitializer>().Single();

        // Runs the initializer the way the host does: StartAsync, then the app-started signal, then waits for the preload.
        private async Task StartAsync(IServiceProvider provider)
        {
            CacheInitializer initializer = GetInitializer(provider);
            await initializer.StartAsync(CancellationToken.None);
            await _applicationStarted.CancelAsync();
            await initializer.Preloading;
        }

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
            private readonly Func<IFhirRequestContext> _getContext;

            public SecondCache(Func<IFhirRequestContext> getContext)
                : base((_, _, _) => new ValueTask<string>("value"))
            {
                _getContext = getContext;
            }

            public ConcurrentQueue<IFhirRequestContext> Contexts { get; } = new();

            protected override ValueTask<string> GetRemoteDataAsync(string partitionId, bool async, CancellationToken cancellationToken)
            {
                Contexts.Enqueue(_getContext());
                return base.GetRemoteDataAsync(partitionId, async, cancellationToken);
            }
        }

        private sealed class AppCache : GlobalCache<Guid>
        {
            public AppCache()
                : base(NullLogger.Instance)
            {
            }

            public int Loads { get; private set; }

            protected override ValueTask<Guid> GetRemoteDataAsync(bool async, CancellationToken cancellationToken)
            {
                Loads++;
                return new ValueTask<Guid>(Guid.NewGuid());
            }
        }
    }
}
