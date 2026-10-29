// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.SqlServer.Configs;

namespace Microsoft.Health.Fhir.SqlServer.Features.Metrics
{
    internal sealed class SqlConnectionPoolConfigurationMetrics : IHostedService, IDisposable
    {
        internal const string MeterName = "Microsoft.Health.Fhir.SqlServer.ConnectionPool";

        private readonly Meter _meter = new Meter(MeterName);
        private readonly SqlServerDataStoreConfiguration _configuration;
        private readonly ILogger<SqlConnectionPoolConfigurationMetrics> _logger;
        private ObservableGauge<double> _maxPoolSizeGauge;

        public SqlConnectionPoolConfigurationMetrics(
            IOptions<SqlServerDataStoreConfiguration> configuration,
            ILogger<SqlConnectionPoolConfigurationMetrics> logger)
        {
            _configuration = configuration.Value;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            int maxPoolSize;
            try
            {
                maxPoolSize = _configuration.MaxPoolSize
                    ?? new SqlConnectionStringBuilder(_configuration.ConnectionString ?? string.Empty).MaxPoolSize;
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Unable to read Max Pool Size from the SQL connection string. The max-pool-size metric will not be reported.");
                return Task.CompletedTask;
            }

            _maxPoolSizeGauge = _meter.CreateObservableGauge(
                "Sql.ConnectionPool",
                () => new Measurement<double>(maxPoolSize, new KeyValuePair<string, object>("Counter", "max-pool-size")),
                description: "The configured per-pool limit for connections using the SQL retry service.");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose() => _meter.Dispose();
    }
}
