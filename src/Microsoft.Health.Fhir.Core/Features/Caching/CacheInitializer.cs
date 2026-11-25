// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Partitions;

namespace Microsoft.Health.Fhir.Core.Features.Caching
{
    /// <summary>
    /// Loads every registered cache at startup: app-wide caches once with <see cref="GlobalCache{T}.GetAsync"/>, then,
    /// for each partition, every partition cache through <see cref="ScopedCache{T}.GetAsync"/> in a DI scope whose
    /// request context holds that partition.
    /// </summary>
    /// <remarks>
    /// Preloading starts in the background on <see cref="IHostApplicationLifetime.ApplicationStarted"/>, after every
    /// hosted service has started (including schema initialization), so it doesn't depend on registration order. This
    /// works with both the generic host and the web host the server uses, which never calls
    /// <c>IHostedLifecycleService.StartedAsync</c>. A cache read before its preload loads on that read. A failed preload
    /// is logged and doesn't stop startup; that cache loads on first use.
    /// </remarks>
    internal sealed class CacheInitializer : IHostedService, IDisposable
    {
        private readonly CachePreload[] _preloads;
        private readonly IPartitionManager _partitionManager;
        private readonly IServiceProvider _serviceProvider;
        private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly ILogger<CacheInitializer> _logger;
        private readonly CancellationTokenSource _stopping = new();
        private CancellationTokenRegistration _startedRegistration;
        private Task _preloading = Task.CompletedTask;

        public CacheInitializer(
            IEnumerable<CachePreload> preloads,
            IPartitionManager partitionManager,
            IServiceProvider serviceProvider,
            RequestContextAccessor<IFhirRequestContext> requestContextAccessor,
            IHostApplicationLifetime lifetime,
            ILogger<CacheInitializer> logger)
        {
            _preloads = EnsureArg.IsNotNull(preloads, nameof(preloads)).ToArray();
            _partitionManager = EnsureArg.IsNotNull(partitionManager, nameof(partitionManager));
            _serviceProvider = EnsureArg.IsNotNull(serviceProvider, nameof(serviceProvider));
            _requestContextAccessor = EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));
            _lifetime = EnsureArg.IsNotNull(lifetime, nameof(lifetime));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        internal Task Preloading => _preloading;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _startedRegistration = _lifetime.ApplicationStarted.Register(() => _preloading = Task.Run(() => PreloadAllAsync(_stopping.Token)));
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await _stopping.CancelAsync();
            try
            {
                await _preloading.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Shutting down; unfinished preloads don't matter.
            }
        }

        public void Dispose()
        {
            _startedRegistration.Dispose();
            _stopping.Dispose();
        }

        private async Task PreloadAllAsync(CancellationToken cancellationToken)
        {
            foreach (CachePreload preload in _preloads.Where(p => !p.IsPartitioned))
            {
                await PreloadAsync(preload, _serviceProvider, partitionId: null, cancellationToken);
            }

            IReadOnlyCollection<IPartitionInfo> partitions = _partitionManager.GetPartitions();
            foreach (IPartitionInfo partition in partitions.Count > 0 ? partitions : [null])
            {
                await PreloadPartitionAsync(partition, cancellationToken);
            }
        }

        private async Task PreloadPartitionAsync(IPartitionInfo partition, CancellationToken cancellationToken)
        {
            await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();

            // The request context is an AsyncLocal set inside this async method, so it reverts when the method returns.
            _requestContextAccessor.RequestContext = new FhirRequestContext(
                method: nameof(CacheInitializer),
                uriString: "https://localhost/",
                baseUriString: "https://localhost/",
                correlationId: Guid.NewGuid().ToString(),
                requestHeaders: new Dictionary<string, StringValues>(),
                responseHeaders: new Dictionary<string, StringValues>())
            {
                IsBackgroundTask = true,
                Partition = partition,
            };

            foreach (CachePreload preload in _preloads.Where(p => p.IsPartitioned))
            {
                await PreloadAsync(preload, scope.ServiceProvider, partition?.Id, cancellationToken);
            }
        }

        private async Task PreloadAsync(CachePreload preload, IServiceProvider serviceProvider, string partitionId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await preload.LoadAsync(serviceProvider, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Preloading {Cache} for partition '{PartitionId}' failed. It will load on first use.", preload.Name, partitionId);
            }
        }
    }
}
