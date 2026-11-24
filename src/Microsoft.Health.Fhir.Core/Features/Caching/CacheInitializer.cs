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
    /// Loads every registered cache at startup, so the first requests read memory: app-wide caches once, then
    /// partition caches for every partition.
    /// </summary>
    /// <remarks>
    /// A failed preload is logged and does not stop startup; that cache loads on its first use instead.
    /// </remarks>
    internal sealed class CacheInitializer : IHostedService
    {
        private readonly IReadOnlyList<CachePreload> _preloads;
        private readonly IPartitionManager _partitionManager;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<CacheInitializer> _logger;

        public CacheInitializer(
            IEnumerable<CachePreload> preloads,
            IPartitionManager partitionManager,
            IServiceProvider serviceProvider,
            ILogger<CacheInitializer> logger)
        {
            _preloads = EnsureArg.IsNotNull(preloads, nameof(preloads)).ToList();
            _partitionManager = EnsureArg.IsNotNull(partitionManager, nameof(partitionManager));
            _serviceProvider = EnsureArg.IsNotNull(serviceProvider, nameof(serviceProvider));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        public async Task StartAsync(CancellationToken cancellationToken)
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

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private async Task PreloadAsync(CachePreload preload, IPartitionInfo partition, CancellationToken cancellationToken)
        {
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
