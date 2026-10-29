// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.SqlServer.Features.Metrics;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer.Configs;
using Microsoft.Health.Test.Utilities;
using OpenTelemetry.Metrics;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Metrics
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class SqlConnectionPoolMetricsTests
    {
        [Theory]
        [InlineData(300, "Data Source=localhost;Max Pool Size=150", 300)]
        [InlineData(null, "Data Source=localhost;Max Pool Size=150", 150)]
        [InlineData(null, "Data Source=localhost", 100)]
        public async Task GivenPoolConfiguration_WhenMetricsAreCollected_ThenEffectivePerPoolMaximumIsReported(
            int? configuredMax,
            string connectionString,
            double expectedMax)
        {
            var configuration = Options.Create(new SqlServerDataStoreConfiguration { ConnectionString = connectionString, MaxPoolSize = configuredMax });
            using var metrics = new SqlConnectionPoolConfigurationMetrics(configuration, NullLogger<SqlConnectionPoolConfigurationMetrics>.Instance);
            double? actualMax = null;
            using var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == SqlConnectionPoolConfigurationMetrics.MeterName && instrument.Name == "Sql.ConnectionPool")
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
            {
                foreach (KeyValuePair<string, object> tag in tags)
                {
                    if (tag.Key == "Counter" && Equals(tag.Value, "max-pool-size"))
                    {
                        actualMax = value;
                    }
                }
            });
            listener.Start();

            await metrics.StartAsync(default);
            listener.RecordObservableInstruments();

            Assert.Equal(expectedMax, actualMax);
        }

        [Fact]
        public async Task GivenSqlMetricsRegistration_WhenSqlClientRuns_ThenOpenTelemetryExportsPoolMetrics()
        {
            var exported = new List<Metric>();
            _ = new SqlConnectionStringBuilder("Data Source=localhost;Encrypt=True");
            using IHost host = new HostBuilder()
                .ConfigureServices(services =>
                {
                    services.Configure<SqlServerDataStoreConfiguration>(options =>
                    {
                        options.ConnectionString = "Data Source=localhost;Encrypt=True";
                        options.MaxPoolSize = 200;
                    });
                    services.AddHostedService<SqlConnectionPoolConfigurationMetrics>();
                    services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddSqlConnectionPoolMetrics(refreshIntervalSeconds: 1).AddInMemoryExporter(exported));
                })
                .Build();

            await host.StartAsync();
            try
            {
                using (new SqlConnection("Data Source=localhost;Encrypt=True"))
                {
                    SqlConnection.ClearAllPools();
                }

                MeterProvider meterProvider = host.Services.GetRequiredService<MeterProvider>();
                var stopwatch = Stopwatch.StartNew();
                while (stopwatch.Elapsed < TimeSpan.FromSeconds(12))
                {
                    meterProvider.ForceFlush();
                    if (exported.Any(metric => metric.Name.EndsWith(".active-soft-connects", StringComparison.Ordinal))
                        && exported.Any(metric => metric.Name.EndsWith(".hard-connects", StringComparison.Ordinal)))
                    {
                        break;
                    }

                    await Task.Delay(100);
                }

                string diagnostics = string.Join(", ", exported.Select(metric => $"{metric.MeterName}/{metric.Name}:{metric.MetricType}").Distinct());
                Assert.True(
                    exported.Any(metric => metric.Name.EndsWith(".active-soft-connects", StringComparison.Ordinal) && metric.MetricType == MetricType.DoubleGauge),
                    $"Expected a SqlClient pool gauge; exported metrics: {diagnostics}");
                Assert.Contains(exported, metric => metric.Name.EndsWith(".hard-connects", StringComparison.Ordinal) && metric.MetricType == MetricType.DoubleSum);
                Assert.Contains(exported, metric => metric.Name == "Sql.ConnectionPool" && metric.MetricType == MetricType.DoubleGauge);
            }
            finally
            {
                await host.StopAsync();
            }
        }
    }
}
