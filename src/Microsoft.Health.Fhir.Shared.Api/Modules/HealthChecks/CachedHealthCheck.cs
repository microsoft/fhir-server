// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core;

namespace Microsoft.Health.Fhir.Api.Modules.HealthChecks
{
    internal sealed class CachedHealthCheck : IHealthCheck, IDisposable
    {
        private readonly IServiceProvider _provider;
        private readonly Func<IServiceProvider, IHealthCheck> _healthCheck;
        private readonly ILogger<CachedHealthCheck> _logger;
        private DateTimeOffset _lastChecked;
        private HealthCheckResult _lastResult;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private static readonly TimeSpan CacheTime = TimeSpan.FromSeconds(1);

        public CachedHealthCheck(IServiceProvider provider, Func<IServiceProvider, IHealthCheck> healthCheck, ILogger<CachedHealthCheck> logger)
        {
            EnsureArg.IsNotNull(provider, nameof(provider));
            EnsureArg.IsNotNull(healthCheck, nameof(healthCheck));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _provider = provider;
            _healthCheck = healthCheck;
            _logger = logger;
        }

        private static DateTimeOffset ExpirationWindow => Clock.UtcNow.Add(-CacheTime);

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
        {
            if (_lastChecked >= ExpirationWindow)
            {
                return _lastResult;
            }

            try
            {
                await _semaphore.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException oce) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation(oce, $"Cancellation was requested for {nameof(CheckHealthAsync)}");
                return _lastResult;
            }

            try
            {
                if (_lastChecked >= ExpirationWindow)
                {
                    return _lastResult;
                }

                using (IServiceScope scope = _provider.CreateScope())
                {
                    try
                    {
                        IHealthCheck check = _healthCheck.Invoke(scope.ServiceProvider);
                        _lastResult = await check.CheckHealthAsync(context, cancellationToken);
                        _lastChecked = Clock.UtcNow;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, Resources.FailedHealthCheckMessage);
                        _lastResult = HealthCheckResult.Unhealthy(Resources.FailedHealthCheckMessage);
                        _lastChecked = Clock.UtcNow;
                    }
                }

                return _lastResult;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public void Dispose()
        {
            _semaphore?.Dispose();
        }
    }
}
