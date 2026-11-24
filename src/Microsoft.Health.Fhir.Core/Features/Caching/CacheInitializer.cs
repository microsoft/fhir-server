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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Partitions;

namespace Microsoft.Health.Fhir.Core.Features.Caching
{
    /// <summary>
    /// Loads every registered cache once the app has started: app-wide caches once, then partition caches for
    /// every partition.
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
        private readonly IReadOnlyList<CachePreload> _preloads;
        private readonly IPartitionManager _partitionManager;
        private readonly IServiceProvider _serviceProvider;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly ILogger<CacheInitializer> _logger;
        private readonly CancellationTokenSource _stopping = new();
        private CancellationTokenRegistration _startedRegistration;
        private Task _preloading = Task.CompletedTask;

        public CacheInitializer(
            IEnumerable<CachePreload> preloads,
            IPartitionManager partitionManager,
            IServiceProvider serviceProvider,
            IHostApplicationLifetime lifetime,
            ILogger<CacheInitializer> logger)
        {
            _preloads = EnsureArg.IsNotNull(preloads, nameof(preloads)).ToList();
            _partitionManager = EnsureArg.IsNotNull(partitionManager, nameof(partitionManager));
            _serviceProvider = EnsureArg.IsNotNull(serviceProvider, nameof(serviceProvider));
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

        internal async Task PreloadAllAsync(CancellationToken cancellationToken)
        {
            foreach (CachePreload preload in _preloads.Where(p => !p.IsPartitioned))
            {
                await PreloadAsync(preload, partition: null, cancellationToken);
            }

            IEnumerable<IPartitionInfo> partitions = _partitionManager.IsEnabled ? _partitionManager.GetPartitions() : [null];
            foreach (IPartitionInfo partition in partitions)
            {
                foreach (CachePreload preload in _preloads.Where(p => p.IsPartitioned))
                {
                    await PreloadAsync(preload, partition, cancellationToken);
                }
            }
        }

        private async Task PreloadAsync(CachePreload preload, IPartitionInfo partition, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await preload.LoadAsync(_serviceProvider, partition, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Preloading {Cache} for partition '{PartitionId}' failed. It will load on first use.", preload.Name, partition?.Id);
            }
        }
    }
}
