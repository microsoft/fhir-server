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
using Prometheus;
using Prometheus.DotNetRuntime;

namespace Microsoft.Health.Fhir.Web
{
    public sealed class PrometheusMetricsServer : IHostedService, IDisposable
    {
        private readonly PrometheusMetricsConfig _prometheusMetricsConfig;
        private readonly ILogger<PrometheusMetricsServer> _logger;
        private KestrelMetricServer _metricsServer;

        public PrometheusMetricsServer(IOptions<PrometheusMetricsConfig> config, ILogger<PrometheusMetricsServer> logger)
        {
            _prometheusMetricsConfig = config.Value;
            _logger = logger;
        }

        Task IHostedService.StartAsync(CancellationToken cancellationToken)
        {
            if (_prometheusMetricsConfig.Enabled)
            {
                _logger.LogInformation("Starting Prometheus Metrics server on port {port} with endpoint {endpoint}", _prometheusMetricsConfig.Port, _prometheusMetricsConfig.Path);
                _metricsServer = new KestrelMetricServer(port: _prometheusMetricsConfig.Port, url: _prometheusMetricsConfig.Path);

                if (_prometheusMetricsConfig.DotnetRuntimeMetrics)
                {
                    DotNetRuntimeStatsBuilder.Customize()
                        .WithThreadPoolSchedulingStats()
                        .WithContentionStats()
                        .WithGcStats()
                        .WithJitStats()
                        .WithThreadPoolStats()
                        .StartCollecting();
                }

                _metricsServer.Start();
            }

            return Task.CompletedTask;
        }

        Task IHostedService.StopAsync(CancellationToken cancellationToken)
        {
            Dispose();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _metricsServer?.Stop();
        }
    }
}
