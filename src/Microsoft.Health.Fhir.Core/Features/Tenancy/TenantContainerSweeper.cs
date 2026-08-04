// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Health.Fhir.Core.Features.Tenancy
{
    /// <summary>
    /// Periodically evicts tenant containers that have remained idle longer than the configured timeout.
    /// </summary>
    public sealed class TenantContainerSweeper : BackgroundService
    {
        private readonly ITenantContainerCache _cache;
        private readonly TenantContainerCacheOptions _options;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<TenantContainerSweeper> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="TenantContainerSweeper"/> class.
        /// </summary>
        /// <param name="cache">The tenant container cache to sweep.</param>
        /// <param name="options">The cache options.</param>
        /// <param name="timeProvider">The time provider used for scheduling.</param>
        /// <param name="logger">The logger.</param>
        public TenantContainerSweeper(
            ITenantContainerCache cache,
            IOptions<TenantContainerCacheOptions> options,
            TimeProvider timeProvider,
            ILogger<TenantContainerSweeper> logger)
        {
            ArgumentNullException.ThrowIfNull(cache);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(timeProvider);
            ArgumentNullException.ThrowIfNull(logger);

            _cache = cache;
            _options = options.Value;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(_options.SweepInterval, _timeProvider);

            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                {
                    try
                    {
                        await _cache.EvictIdleAsync(stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
#pragma warning disable CA1031 // Sweep failures must not take the host down.
                    catch (Exception exception)
#pragma warning restore CA1031
                    {
                        _logger.LogWarning(exception, "Tenant container sweep failed. It will be retried.");
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
